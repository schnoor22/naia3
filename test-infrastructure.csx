using Npgsql;
using StackExchange.Redis;
using Confluent.Kafka;
using System.Net.Http.Json;

Console.WriteLine("NAIA v3 - Infrastructure Connectivity Test");
Console.WriteLine("==========================================\n");

// Test PostgreSQL
try
{
    var connString = "Host=localhost;Port=5432;Database=naia;Username=naia;Password=naia_dev_password";
    await using var conn = new NpgsqlConnection(connString);
    await conn.OpenAsync();
    
    var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM points", conn);
    var count = await cmd.ExecuteScalarAsync();
    
    Console.WriteLine($"✅ PostgreSQL: Connected (Points table exists, {count} records)");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ PostgreSQL: {ex.Message}");
}

// Test Redis
try
{
    var redis = ConnectionMultiplexer.Connect("localhost:6379");
    var db = redis.GetDatabase();
    await db.StringSetAsync("naia:test", "hello");
    var value = await db.StringGetAsync("naia:test");
    
    Console.WriteLine($"✅ Redis: Connected (Test write successful: {value})");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Redis: {ex.Message}");
}

// Test Kafka
try
{
    var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
    using var producer = new ProducerBuilder<string, string>(config).Build();
    
    var result = await producer.ProduceAsync("naia.datapoints", 
        new Message<string, string> { Key = "test", Value = "hello" });
    
    Console.WriteLine($"✅ Kafka: Connected (Test message sent to partition {result.Partition})");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Kafka: {ex.Message}");
}

// Test QuestDB REST API
try
{
    using var http = new HttpClient();
    var response = await http.GetAsync("http://localhost:9000/exec?query=SELECT COUNT(*) FROM point_data");
    var content = await response.Content.ReadAsStringAsync();
    
    Console.WriteLine($"✅ QuestDB: Connected (REST API responsive)");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ QuestDB: {ex.Message}");
}

Console.WriteLine("\n✨ Infrastructure check complete!");
