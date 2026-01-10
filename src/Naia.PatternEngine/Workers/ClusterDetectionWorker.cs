using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naia.PatternEngine.Configuration;
using Naia.PatternEngine.Events;
using Naia.PatternEngine.Services;
using StackExchange.Redis;

namespace Naia.PatternEngine.Workers;

/// <summary>
/// Consumes CorrelationsUpdated events and detects behavioral clusters
/// using graph-based community detection (Louvain algorithm).
/// 
/// Points that correlate strongly with each other form natural clusters
/// that represent related equipment, processes, or systems.
/// </summary>
public sealed class ClusterDetectionWorker : BaseKafkaConsumer<CorrelationsUpdated>
{
    private readonly IPatternEventPublisher _eventPublisher;
    private readonly IConnectionMultiplexer _redis;
    private readonly ClusterDetectionOptions _options;
    private readonly PatternKafkaOptions _kafkaOptions;
    
    // Correlation graph: PointId -> (CorrelatedPointId -> Correlation)
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, double>> _correlationGraph = new();
    
    private readonly Timer _clusterTimer;
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private HashSet<Guid> _dirtyNodes = new();
    private readonly object _dirtyLock = new();

    public ClusterDetectionWorker(
        ILogger<ClusterDetectionWorker> logger,
        IOptions<PatternFlywheelOptions> options,
        IPatternEventPublisher eventPublisher,
        IConnectionMultiplexer redis)
        : base(
            logger,
            options.Value.Kafka.BootstrapServers,
            options.Value.Kafka.ClusterDetectionGroupId,
            options.Value.Kafka.CorrelationsUpdatedTopic)
    {
        _eventPublisher = eventPublisher;
        _redis = redis;
        _options = options.Value.ClusterDetection;
        _kafkaOptions = options.Value.Kafka;
        
        // Run clustering every 60 seconds
        _clusterTimer = new Timer(
            ClusterTimerCallback,
            null,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60));
    }

    protected override Task ProcessMessageAsync(
        CorrelationsUpdated message,
        string key,
        CancellationToken cancellationToken)
    {
        // Update correlation graph for each pair of points
        if (message.PointIds.Count >= 2)
        {
            for (var i = 0; i < message.PointIds.Count - 1; i++)
            {
                for (var j = i + 1; j < message.PointIds.Count; j++)
                {
                    UpdateCorrelationGraph(message.PointIds[i], message.PointIds[j], message.AverageCorrelation);
                }
            }
            
            // Mark nodes as dirty for next clustering run
            lock (_dirtyLock)
            {
                foreach (var pointId in message.PointIds)
                {
                    _dirtyNodes.Add(pointId);
                }
            }
        }

        return Task.CompletedTask;
    }

    private void UpdateCorrelationGraph(Guid point1, Guid point2, double correlation)
    {
        // Bidirectional edge
        var edges1 = _correlationGraph.GetOrAdd(point1, _ => new ConcurrentDictionary<Guid, double>());
        var edges2 = _correlationGraph.GetOrAdd(point2, _ => new ConcurrentDictionary<Guid, double>());

        edges1[point2] = correlation;
        edges2[point1] = correlation;
    }

    private async void ClusterTimerCallback(object? state)
    {
        if (!await _processLock.WaitAsync(0))
            return;

        try
        {
            await DetectClustersAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in cluster detection timer callback");
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task DetectClustersAsync(CancellationToken cancellationToken)
    {
        // Get dirty nodes and clear
        HashSet<Guid> nodesToProcess;
        lock (_dirtyLock)
        {
            if (_dirtyNodes.Count == 0) return;
            nodesToProcess = _dirtyNodes;
            _dirtyNodes = new HashSet<Guid>();
        }

        Logger.LogDebug("Running cluster detection on {Count} dirty nodes", nodesToProcess.Count);

        // Run clustering algorithm
        var clusters = _options.Algorithm.ToLowerInvariant() switch
        {
            "louvain" => DetectClustersLouvain(nodesToProcess),
            "dbscan" => DetectClustersDbscan(nodesToProcess),
            _ => DetectClustersLouvain(nodesToProcess)
        };

        var db = _redis.GetDatabase();
        var publishBatch = new List<(string Key, ClusterCreated Event)>();

        foreach (var cluster in clusters)
        {
            // Check if this cluster already exists
            var clusterKey = GetClusterKey(cluster.Members);
            var existingCluster = await db.StringGetAsync($"naia:cluster:{clusterKey}");

            if (existingCluster.HasValue)
            {
                // Cluster already exists, check for significant changes
                continue;
            }

            // Validate cluster
            if (cluster.Members.Count < _options.MinClusterSize ||
                cluster.Members.Count > _options.MaxClusterSize ||
                cluster.Cohesion < _options.MinCohesion)
            {
                continue;
            }

            // Create event
            var evt = new ClusterCreated
            {
                ClusterId = Guid.NewGuid(),
                SourceType = ClusterSourceType.Continuous,
                PointIds = cluster.Members.ToList(),
                PointNames = cluster.Members.Select(m => m.ToString()).ToList(), // TODO: Look up actual names
                AverageCorrelation = cluster.Cohesion,
                CohesionScore = cluster.Cohesion,
                CreatedAt = DateTime.UtcNow
            };

            publishBatch.Add((evt.ClusterId.ToString(), evt));

            // Cache the cluster
            await db.StringSetAsync(
                $"naia:cluster:{clusterKey}",
                evt.ClusterId.ToString(),
                TimeSpan.FromHours(24));
        }

        if (publishBatch.Count > 0)
        {
            await _eventPublisher.PublishBatchAsync(
                _kafkaOptions.ClustersCreatedTopic,
                publishBatch,
                cancellationToken);

            Logger.LogInformation(
                "Published {Count} ClusterCreated events",
                publishBatch.Count);
        }
    }

    /// <summary>
    /// Louvain community detection algorithm for finding clusters.
    /// Optimizes modularity by iteratively merging nodes into communities.
    /// </summary>
    private List<ClusterInfo> DetectClustersLouvain(HashSet<Guid> seedNodes)
    {
        // Build subgraph containing seed nodes and their neighbors
        var relevantNodes = new HashSet<Guid>(seedNodes);
        foreach (var node in seedNodes)
        {
            if (_correlationGraph.TryGetValue(node, out var edges))
            {
                foreach (var neighbor in edges.Keys)
                {
                    relevantNodes.Add(neighbor);
                }
            }
        }

        if (relevantNodes.Count < _options.MinClusterSize)
            return new List<ClusterInfo>();

        // Initialize: each node is its own community
        var nodeToCommmunity = new Dictionary<Guid, int>();
        var communityIndex = 0;
        foreach (var node in relevantNodes)
        {
            nodeToCommmunity[node] = communityIndex++;
        }

        // Calculate total edge weight
        var totalWeight = CalculateTotalWeight(relevantNodes);
        if (totalWeight == 0) return new List<ClusterInfo>();

        var improved = true;
        var maxIterations = 100;
        var iteration = 0;

        while (improved && iteration < maxIterations)
        {
            improved = false;
            iteration++;

            foreach (var node in relevantNodes)
            {
                var currentCommunity = nodeToCommmunity[node];
                var bestCommunity = currentCommunity;
                var bestDeltaQ = 0.0;

                // Get neighboring communities
                var neighborCommunities = GetNeighborCommunities(node, nodeToCommmunity);

                foreach (var targetCommunity in neighborCommunities)
                {
                    if (targetCommunity == currentCommunity) continue;

                    var deltaQ = CalculateModularityGain(
                        node, currentCommunity, targetCommunity,
                        nodeToCommmunity, totalWeight, relevantNodes);

                    if (deltaQ > bestDeltaQ)
                    {
                        bestDeltaQ = deltaQ;
                        bestCommunity = targetCommunity;
                    }
                }

                if (bestCommunity != currentCommunity)
                {
                    nodeToCommmunity[node] = bestCommunity;
                    improved = true;
                }
            }
        }

        // Group nodes by community
        var communities = nodeToCommmunity
            .GroupBy(x => x.Value)
            .Select(g => new ClusterInfo
            {
                Members = g.Select(x => x.Key).ToHashSet()
            })
            .Where(c => c.Members.Count >= _options.MinClusterSize &&
                        c.Members.Count <= _options.MaxClusterSize)
            .ToList();

        // Calculate cohesion for each cluster
        foreach (var cluster in communities)
        {
            CalculateClusterCohesion(cluster);
        }

        return communities;
    }

    /// <summary>
    /// DBSCAN clustering based on correlation distance.
    /// </summary>
    private List<ClusterInfo> DetectClustersDbscan(HashSet<Guid> seedNodes)
    {
        var clusters = new List<ClusterInfo>();
        var visited = new HashSet<Guid>();
        var clustered = new HashSet<Guid>();

        foreach (var point in seedNodes)
        {
            if (visited.Contains(point)) continue;
            visited.Add(point);

            var neighbors = GetNeighbors(point, seedNodes);
            
            if (neighbors.Count < _options.DbscanMinPoints)
                continue;

            // Start new cluster
            var cluster = new ClusterInfo { Members = new HashSet<Guid> { point } };
            clustered.Add(point);

            // Expand cluster
            var seedQueue = new Queue<Guid>(neighbors);
            while (seedQueue.Count > 0)
            {
                var current = seedQueue.Dequeue();
                
                if (!visited.Contains(current))
                {
                    visited.Add(current);
                    var currentNeighbors = GetNeighbors(current, seedNodes);
                    
                    if (currentNeighbors.Count >= _options.DbscanMinPoints)
                    {
                        foreach (var neighbor in currentNeighbors)
                        {
                            seedQueue.Enqueue(neighbor);
                        }
                    }
                }

                if (!clustered.Contains(current))
                {
                    cluster.Members.Add(current);
                    clustered.Add(current);
                }
            }

            if (cluster.Members.Count >= _options.MinClusterSize &&
                cluster.Members.Count <= _options.MaxClusterSize)
            {
                CalculateClusterCohesion(cluster);
                clusters.Add(cluster);
            }
        }

        return clusters;
    }

    private HashSet<Guid> GetNeighbors(Guid point, HashSet<Guid> candidates)
    {
        var neighbors = new HashSet<Guid>();
        
        if (!_correlationGraph.TryGetValue(point, out var edges))
            return neighbors;

        foreach (var (neighbor, correlation) in edges)
        {
            if (!candidates.Contains(neighbor)) continue;
            
            // Distance = 1 - |correlation|
            var distance = 1.0 - Math.Abs(correlation);
            if (distance <= _options.DbscanEpsilon)
            {
                neighbors.Add(neighbor);
            }
        }

        return neighbors;
    }

    private HashSet<int> GetNeighborCommunities(Guid node, Dictionary<Guid, int> nodeToCommmunity)
    {
        var communities = new HashSet<int>();
        
        if (_correlationGraph.TryGetValue(node, out var edges))
        {
            foreach (var neighbor in edges.Keys)
            {
                if (nodeToCommmunity.TryGetValue(neighbor, out var community))
                {
                    communities.Add(community);
                }
            }
        }

        return communities;
    }

    private double CalculateTotalWeight(HashSet<Guid> nodes)
    {
        var total = 0.0;
        
        foreach (var node in nodes)
        {
            if (_correlationGraph.TryGetValue(node, out var edges))
            {
                foreach (var (neighbor, weight) in edges)
                {
                    if (nodes.Contains(neighbor))
                    {
                        total += Math.Abs(weight);
                    }
                }
            }
        }

        return total / 2; // Each edge counted twice
    }

    private double CalculateModularityGain(
        Guid node,
        int fromCommunity,
        int toCommunity,
        Dictionary<Guid, int> nodeToCommmunity,
        double totalWeight,
        HashSet<Guid> relevantNodes)
    {
        // Simplified modularity gain calculation
        var sumIn = 0.0;
        var sumTot = 0.0;
        var ki = 0.0;
        var kiIn = 0.0;

        if (_correlationGraph.TryGetValue(node, out var nodeEdges))
        {
            ki = nodeEdges.Where(e => relevantNodes.Contains(e.Key))
                         .Sum(e => Math.Abs(e.Value));

            foreach (var (neighbor, weight) in nodeEdges)
            {
                if (!relevantNodes.Contains(neighbor)) continue;
                if (!nodeToCommmunity.TryGetValue(neighbor, out var neighborComm)) continue;

                if (neighborComm == toCommunity)
                {
                    kiIn += Math.Abs(weight);
                }
            }
        }

        // Get community totals
        foreach (var (n, comm) in nodeToCommmunity)
        {
            if (comm == toCommunity && _correlationGraph.TryGetValue(n, out var edges))
            {
                sumTot += edges.Where(e => relevantNodes.Contains(e.Key))
                              .Sum(e => Math.Abs(e.Value));
            }
        }

        if (totalWeight == 0) return 0;

        var m2 = totalWeight * 2;
        var deltaQ = (kiIn / m2) - (sumTot * ki / (m2 * m2));

        return deltaQ;
    }

    private void CalculateClusterCohesion(ClusterInfo cluster)
    {
        var correlations = new List<double>();

        var members = cluster.Members.ToList();
        for (var i = 0; i < members.Count - 1; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                if (_correlationGraph.TryGetValue(members[i], out var edges) &&
                    edges.TryGetValue(members[j], out var corr))
                {
                    correlations.Add(Math.Abs(corr));
                }
            }
        }

        if (correlations.Count > 0)
        {
            cluster.Cohesion = correlations.Average();
            cluster.MinCorrelation = correlations.Min();
            cluster.MaxCorrelation = correlations.Max();
        }
    }

    private string GetClusterKey(HashSet<Guid> members)
    {
        // Create deterministic key from sorted members
        var sortedIds = members.OrderBy(x => x).Select(x => x.ToString("N").Substring(0, 8));
        return string.Join("-", sortedIds);
    }

    protected override Task OnStoppingAsync(CancellationToken cancellationToken)
    {
        _clusterTimer.Dispose();
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _clusterTimer.Dispose();
        _processLock.Dispose();
        base.Dispose();
    }
}

internal sealed class ClusterInfo
{
    public HashSet<Guid> Members { get; set; } = new();
    public double Cohesion { get; set; }
    public double MinCorrelation { get; set; }
    public double MaxCorrelation { get; set; }
}
