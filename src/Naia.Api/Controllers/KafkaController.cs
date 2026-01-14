using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Naia.Api.Controllers;

/// <summary>
/// Kafka management and monitoring endpoints
/// </summary>
[ApiController]
[Route("api/pipeline/kafka")]
public class KafkaController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaController> _logger;

    public KafkaController(IConfiguration configuration, ILogger<KafkaController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// List all Kafka topics with metadata
    /// </summary>
    [HttpGet("topics")]
    public async Task<IActionResult> GetTopics()
    {
        try
        {
            var kafkaBootstrap = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
            
            var config = new AdminClientConfig
            {
                BootstrapServers = kafkaBootstrap,
                SocketTimeoutMs = 5000
            };

            using var adminClient = new AdminClientBuilder(config).Build();
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));

            var topics = metadata.Topics
                .Where(t => !t.Topic.StartsWith("__"))
                .Select(t => new
                {
                    name = t.Topic,
                    partitions = t.Partitions.Count,
                    replicationFactor = t.Partitions.FirstOrDefault()?.Replicas.Length ?? 0,
                    error = t.Error.Code != ErrorCode.NoError ? t.Error.Reason : null
                })
                .ToList();

            // Get consumer group info
            var consumerGroups = new List<object>();
            try
            {
                var groupList = adminClient.ListGroups(TimeSpan.FromSeconds(3));
                foreach (var group in groupList)
                {
                    if (!string.IsNullOrEmpty(group.Group))
                    {
                        consumerGroups.Add(new
                        {
                            groupId = group.Group,
                            state = group.State,
                            protocol = group.ProtocolType,
                            topics = new List<string>(), // Would need separate API call per group
                            lag = 0 // Placeholder - would need to calculate
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get consumer groups");
            }

            return Ok(new { topics, consumerGroups });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Kafka topics");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get recent messages from a topic
    /// </summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] string topic, [FromQuery] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return BadRequest("Topic parameter is required");

        try
        {
            var kafkaBootstrap = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
            
            // First get metadata using admin client
            var adminConfig = new AdminClientConfig
            {
                BootstrapServers = kafkaBootstrap,
                SocketTimeoutMs = 5000
            };

            using var adminClient = new AdminClientBuilder(adminConfig).Build();
            var metadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(5));
            var topicMetadata = metadata.Topics.FirstOrDefault(t => t.Topic == topic);
            
            if (topicMetadata == null)
                return NotFound($"Topic '{topic}' not found");

            // Now consume messages
            var config = new ConsumerConfig
            {
                BootstrapServers = kafkaBootstrap,
                GroupId = $"naia-debug-{Guid.NewGuid()}",
                AutoOffsetReset = AutoOffsetReset.Latest,
                EnableAutoCommit = false,
                SessionTimeoutMs = 6000
            };

            using var consumer = new ConsumerBuilder<string, string>(config).Build();

            var messages = new List<object>();
            
            // Subscribe and seek to end minus limit
            var partitions = topicMetadata.Partitions.Select(p => new TopicPartition(topic, p.PartitionId)).ToList();
            consumer.Assign(partitions);

            // Get watermarks for each partition
            foreach (var partition in partitions)
            {
                var watermark = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(2));
                
                // Seek to get last N messages from this partition
                var messagesPerPartition = Math.Max(1, limit / partitions.Count);
                var startOffset = Math.Max(0, watermark.High.Value - messagesPerPartition);
                
                consumer.Seek(new TopicPartitionOffset(partition, new Offset(startOffset)));
                
                // Consume messages from this partition
                var partitionMessageCount = 0;
                while (partitionMessageCount < messagesPerPartition && messages.Count < limit)
                {
                    var consumeResult = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (consumeResult == null) break;

                    messages.Add(new
                    {
                        topic = consumeResult.Topic,
                        partition = consumeResult.Partition.Value,
                        offset = consumeResult.Offset.Value,
                        timestamp = consumeResult.Message.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        key = consumeResult.Message.Key,
                        value = consumeResult.Message.Value
                    });
                    
                    partitionMessageCount++;
                }
            }

            consumer.Close();

            return Ok(new { messages = messages.OrderByDescending(m => ((dynamic)m).offset).Take(limit).ToList() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get messages from topic {Topic}", topic);
            return StatusCode(500, new { error = ex.Message, details = ex.ToString() });
        }
    }
}
