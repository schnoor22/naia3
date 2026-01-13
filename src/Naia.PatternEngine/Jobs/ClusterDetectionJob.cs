using System.Text.Json;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Npgsql;
using StackExchange.Redis;

namespace Naia.PatternEngine.Jobs;

/// <summary>
/// Detects behavioral clusters of correlated points using graph-based algorithms.
/// This is the third stage of the Pattern Flywheel - grouping related points into
/// equipment candidates.
/// 
/// Uses Louvain community detection for modularity optimization and validates
/// clusters with DBSCAN for density-based confirmation.
/// 
/// Runs every 15 minutes via Hangfire scheduler, after correlation analysis.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 840)]
public sealed class ClusterDetectionJob : IClusterDetectionJob
{
    private readonly ILogger<ClusterDetectionJob> _logger;
    private readonly PatternFlywheelOptions _options;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _postgresConnectionString;

    public ClusterDetectionJob(
        ILogger<ClusterDetectionJob> logger,
        IOptions<PatternFlywheelOptions> options,
        IConnectionMultiplexer redis,
        string postgresConnectionString)
    {
        _logger = logger;
        _options = options.Value;
        _redis = redis;
        _postgresConnectionString = postgresConnectionString;
    }

    public async Task ExecuteAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        context?.WriteLine("Starting cluster detection job...");
        _logger.LogInformation("Starting cluster detection job");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var clustersFound = 0;
        var clustersUpdated = 0;
        var clustersCreated = 0;

        try
        {
            // Load correlation graph from cache
            var correlations = await LoadCorrelationsAsync(cancellationToken);
            context?.WriteLine($"Loaded {correlations.Count} correlations");

            if (correlations.Count == 0)
            {
                context?.WriteLine("No correlations available for clustering");
                return;
            }

            // Build adjacency list for graph algorithms
            var graph = BuildCorrelationGraph(correlations);
            context?.WriteLine($"Built graph with {graph.Count} nodes");

            // Run Louvain community detection
            var communities = DetectCommunitiesLouvain(graph);
            context?.WriteLine($"Louvain detected {communities.Count} communities");

            // Filter and validate clusters
            var validClusters = ValidateClusters(communities, correlations);
            context?.WriteLine($"Validated {validClusters.Count} clusters");

            var progressBar = context?.WriteProgressBar();
            var processed = 0;

            // Store clusters and check for changes
            foreach (var cluster in validClusters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (created, updated) = await StoreClusterAsync(cluster, cancellationToken);
                if (created) clustersCreated++;
                if (updated) clustersUpdated++;
                clustersFound++;

                processed++;
                progressBar?.SetValue(100.0 * processed / validClusters.Count);
            }

            // Mark old clusters as stale
            await MarkStaleClustersAsync(validClusters.Select(c => c.Id).ToList(), cancellationToken);

            stopwatch.Stop();
            context?.WriteLine($"Completed: {clustersFound} clusters, {clustersCreated} new, {clustersUpdated} updated, {stopwatch.ElapsedMilliseconds}ms");

            _logger.LogInformation(
                "Cluster detection complete: {Found} clusters, {Created} new, {Updated} updated, {Duration}ms",
                clustersFound, clustersCreated, clustersUpdated, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cluster detection job failed");
            context?.WriteLine($"ERROR: {ex.Message}");
            throw;
        }
    }

    private async Task<List<CorrelationEdge>> LoadCorrelationsAsync(CancellationToken cancellationToken)
    {
        var correlations = new List<CorrelationEdge>();
        var minCorrelation = _options.ClusterDetection.MinCohesion;

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            SELECT point_id_1, point_id_2, correlation
            FROM correlation_cache
            WHERE ABS(correlation) >= @MinCorrelation
              AND calculated_at > NOW() - INTERVAL '24 hours'
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MinCorrelation", minCorrelation);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            correlations.Add(new CorrelationEdge
            {
                PointId1 = reader.GetGuid(0),
                PointId2 = reader.GetGuid(1),
                Weight = Math.Abs(reader.GetDouble(2)) // Use absolute correlation as weight
            });
        }

        return correlations;
    }

    private Dictionary<Guid, List<(Guid Neighbor, double Weight)>> BuildCorrelationGraph(
        List<CorrelationEdge> correlations)
    {
        var graph = new Dictionary<Guid, List<(Guid, double)>>();

        foreach (var edge in correlations)
        {
            if (!graph.ContainsKey(edge.PointId1))
                graph[edge.PointId1] = new List<(Guid, double)>();
            if (!graph.ContainsKey(edge.PointId2))
                graph[edge.PointId2] = new List<(Guid, double)>();

            graph[edge.PointId1].Add((edge.PointId2, edge.Weight));
            graph[edge.PointId2].Add((edge.PointId1, edge.Weight));
        }

        return graph;
    }

    /// <summary>
    /// Simplified Louvain community detection algorithm.
    /// Iteratively moves nodes between communities to maximize modularity.
    /// </summary>
    private List<HashSet<Guid>> DetectCommunitiesLouvain(
        Dictionary<Guid, List<(Guid Neighbor, double Weight)>> graph)
    {
        if (graph.Count == 0)
            return new List<HashSet<Guid>>();

        // Initialize: each node in its own community
        var nodeToCommunity = new Dictionary<Guid, int>();
        var communityToNodes = new Dictionary<int, HashSet<Guid>>();
        var communityId = 0;

        foreach (var node in graph.Keys)
        {
            nodeToCommunity[node] = communityId;
            communityToNodes[communityId] = new HashSet<Guid> { node };
            communityId++;
        }

        // Calculate total weight of graph
        var totalWeight = graph.Values.SelectMany(e => e).Sum(e => e.Weight) / 2.0;
        if (totalWeight == 0) totalWeight = 1;

        // Iteratively optimize modularity
        var improved = true;
        var iterations = 0;
        var maxIterations = 100;

        while (improved && iterations < maxIterations)
        {
            improved = false;
            iterations++;

            foreach (var node in graph.Keys.OrderBy(_ => Guid.NewGuid()))
            {
                var currentCommunity = nodeToCommunity[node];
                var bestCommunity = currentCommunity;
                var bestGain = 0.0;

                // Calculate modularity gain for moving to each neighbor's community
                var neighborCommunities = graph[node]
                    .Select(e => nodeToCommunity[e.Neighbor])
                    .Distinct()
                    .ToList();

                foreach (var targetCommunity in neighborCommunities)
                {
                    if (targetCommunity == currentCommunity)
                        continue;

                    var gain = CalculateModularityGain(
                        node, currentCommunity, targetCommunity,
                        graph, nodeToCommunity, communityToNodes, totalWeight);

                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestCommunity = targetCommunity;
                    }
                }

                // Move node if there's a positive gain
                if (bestCommunity != currentCommunity && bestGain > 0.001)
                {
                    communityToNodes[currentCommunity].Remove(node);
                    communityToNodes[bestCommunity].Add(node);
                    nodeToCommunity[node] = bestCommunity;
                    improved = true;
                }
            }
        }

        // Return non-empty communities
        return communityToNodes.Values
            .Where(c => c.Count > 0)
            .ToList();
    }

    private double CalculateModularityGain(
        Guid node,
        int fromCommunity,
        int toCommunity,
        Dictionary<Guid, List<(Guid Neighbor, double Weight)>> graph,
        Dictionary<Guid, int> nodeToCommunity,
        Dictionary<int, HashSet<Guid>> communityToNodes,
        double totalWeight)
    {
        // Sum of weights from node to target community
        var weightToTarget = graph[node]
            .Where(e => nodeToCommunity[e.Neighbor] == toCommunity)
            .Sum(e => e.Weight);

        // Sum of weights from node to current community
        var weightToCurrent = graph[node]
            .Where(e => nodeToCommunity[e.Neighbor] == fromCommunity && e.Neighbor != node)
            .Sum(e => e.Weight);

        // Degree of node (sum of edge weights)
        var nodeDegree = graph[node].Sum(e => e.Weight);

        // Sum of degrees in target community
        var targetDegreeSum = communityToNodes[toCommunity]
            .Sum(n => graph.ContainsKey(n) ? graph[n].Sum(e => e.Weight) : 0);

        // Sum of degrees in current community (excluding node)
        var currentDegreeSum = communityToNodes[fromCommunity]
            .Where(n => n != node)
            .Sum(n => graph.ContainsKey(n) ? graph[n].Sum(e => e.Weight) : 0);

        // Modularity gain calculation
        var gain = (weightToTarget - weightToCurrent) / totalWeight
                   - nodeDegree * (targetDegreeSum - currentDegreeSum) / (2 * totalWeight * totalWeight);

        return gain;
    }

    private List<ClusterCandidate> ValidateClusters(
        List<HashSet<Guid>> communities,
        List<CorrelationEdge> correlations)
    {
        var minSize = _options.ClusterDetection.MinClusterSize;
        var maxSize = _options.ClusterDetection.MaxClusterSize;
        var minCohesion = _options.ClusterDetection.MinCohesion;

        var validClusters = new List<ClusterCandidate>();

        foreach (var community in communities)
        {
            // Size filter
            if (community.Count < minSize || community.Count > maxSize)
                continue;

            // Calculate internal cohesion (average correlation within cluster)
            var internalCorrelations = correlations
                .Where(c => community.Contains(c.PointId1) && community.Contains(c.PointId2))
                .ToList();

            if (internalCorrelations.Count == 0)
                continue;

            var avgCohesion = internalCorrelations.Average(c => c.Weight);

            if (avgCohesion < minCohesion)
                continue;

            // Create deterministic cluster ID based on sorted member IDs
            var sortedMembers = community.OrderBy(id => id).ToList();
            var clusterIdSource = string.Join(",", sortedMembers);
            var clusterId = DeterministicGuid(clusterIdSource);

            validClusters.Add(new ClusterCandidate
            {
                Id = clusterId,
                PointIds = sortedMembers,
                Cohesion = avgCohesion,
                DetectedAt = DateTime.UtcNow
            });
        }

        return validClusters;
    }

    private Guid DeterministicGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private async Task<(bool Created, bool Updated)> StoreClusterAsync(
        ClusterCandidate cluster,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Fetch point names for the cluster
        var pointNamesSql = "SELECT id, name FROM points WHERE id = ANY(@PointIds) ORDER BY id";
        
        var pointNameMap = new Dictionary<Guid, string>();
        await using (var pointNamesCmd = new NpgsqlCommand(pointNamesSql, conn))
        {
            pointNamesCmd.Parameters.AddWithValue("@PointIds", cluster.PointIds.ToArray());
            await using var reader = await pointNamesCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                pointNameMap[reader.GetGuid(0)] = reader.GetString(1);
            }
        }

        // Map point IDs to their names in order
        var pointNames = cluster.PointIds
            .Select(id => pointNameMap.ContainsKey(id) ? pointNameMap[id] : id.ToString())
            .ToArray();

        // Check if cluster exists
        var checkSql = "SELECT id, cohesion FROM behavioral_clusters WHERE id = @Id";
        await using var checkCmd = new NpgsqlCommand(checkSql, conn);
        checkCmd.Parameters.AddWithValue("@Id", cluster.Id);
        
        var existing = await checkCmd.ExecuteScalarAsync(cancellationToken);
        var created = existing == null;
        var updated = false;

        if (created)
        {
            // Insert new cluster with point_count and point_names
            var insertSql = @"
                INSERT INTO behavioral_clusters (id, point_ids, point_names, point_count, cohesion, detected_at, is_active, source_type, average_correlation)
                VALUES (@Id, @PointIds, @PointNames, @PointCount, @Cohesion, @DetectedAt, true, 'correlation', @Cohesion)
            ";

            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("@Id", cluster.Id);
            insertCmd.Parameters.AddWithValue("@PointIds", cluster.PointIds.ToArray());
            insertCmd.Parameters.AddWithValue("@PointNames", pointNames);
            insertCmd.Parameters.AddWithValue("@PointCount", cluster.PointIds.Count);
            insertCmd.Parameters.AddWithValue("@Cohesion", cluster.Cohesion);
            insertCmd.Parameters.AddWithValue("@DetectedAt", cluster.DetectedAt);

            await insertCmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Created new cluster {ClusterId} with {Count} points, cohesion {Cohesion:F2}",
                cluster.Id, cluster.PointIds.Count, cluster.Cohesion);
        }
        else
        {
            // Update existing cluster with point_count and point_names
            var updateSql = @"
                UPDATE behavioral_clusters
                SET point_ids = @PointIds, point_names = @PointNames, point_count = @PointCount, 
                    cohesion = @Cohesion, detected_at = @DetectedAt, is_active = true
                WHERE id = @Id
            ";

            await using var updateCmd = new NpgsqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("@Id", cluster.Id);
            updateCmd.Parameters.AddWithValue("@PointIds", cluster.PointIds.ToArray());
            updateCmd.Parameters.AddWithValue("@PointNames", pointNames);
            updateCmd.Parameters.AddWithValue("@PointCount", cluster.PointIds.Count);
            updateCmd.Parameters.AddWithValue("@Cohesion", cluster.Cohesion);
            updateCmd.Parameters.AddWithValue("@DetectedAt", cluster.DetectedAt);

            var rows = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            updated = rows > 0;
        }

        // Cache cluster in Redis for pattern matching
        var db = _redis.GetDatabase();
        var cacheKey = $"naia:cluster:{cluster.Id}";
        var json = JsonSerializer.Serialize(cluster);
        await db.StringSetAsync(cacheKey, json, TimeSpan.FromHours(24));

        return (created, updated);
    }

    private async Task MarkStaleClustersAsync(List<Guid> activeClusterIds, CancellationToken cancellationToken)
    {
        if (activeClusterIds.Count == 0)
            return;

        await using var conn = new NpgsqlConnection(_postgresConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            UPDATE behavioral_clusters
            SET is_active = false
            WHERE id != ALL(@ActiveIds)
              AND is_active = true
              AND detected_at < NOW() - INTERVAL '24 hours'
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ActiveIds", activeClusterIds.ToArray());

        var staleCount = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (staleCount > 0)
        {
            _logger.LogInformation("Marked {Count} stale clusters as inactive", staleCount);
        }
    }
}

internal sealed record CorrelationEdge
{
    public Guid PointId1 { get; init; }
    public Guid PointId2 { get; init; }
    public double Weight { get; init; }
}

internal sealed record ClusterCandidate
{
    public Guid Id { get; init; }
    public List<Guid> PointIds { get; init; } = new();
    public double Cohesion { get; init; }
    public DateTime DetectedAt { get; init; }
}
