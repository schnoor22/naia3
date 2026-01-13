namespace Naia.Connectors.Replay;

/// <summary>
/// Configuration options for the Wind Farm Replay connector.
/// Replays historical Kelmarsh wind farm data through Kafka at configurable speed.
/// </summary>
public sealed class ReplayOptions
{
    public const string SectionName = "WindFarmReplay";
    
    /// <summary>
    /// Enable/disable the replay connector.
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Start replay automatically when the system starts.
    /// </summary>
    public bool AutoStart { get; set; } = true;
    
    /// <summary>
    /// Path to the Kelmarsh data directory containing turbine CSV files.
    /// </summary>
    public string DataDirectory { get; set; } = "data/kelmarsh";
    
    /// <summary>
    /// Site name/code prefix for point names (e.g., "KSH" for Kelmarsh).
    /// </summary>
    public string SiteCode { get; set; } = "KSH";
    
    /// <summary>
    /// Human-readable site name.
    /// </summary>
    public string SiteName { get; set; } = "Kelmarsh Wind Farm";
    
    /// <summary>
    /// Number of turbines to replay (1-6 for Kelmarsh).
    /// </summary>
    public int TurbineCount { get; set; } = 6;
    
    /// <summary>
    /// Speed multiplier for replay. 1.0 = real-time, 10.0 = 10x faster, etc.
    /// Use higher values for testing/demo, 1.0 for realistic simulation.
    /// </summary>
    public double SpeedMultiplier { get; set; } = 1.0;
    
    /// <summary>
    /// Original data interval in minutes (Kelmarsh data is 10-minute intervals).
    /// </summary>
    public int DataIntervalMinutes { get; set; } = 10;
    
    /// <summary>
    /// Maximum batch size for Kafka publishing.
    /// </summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>
    /// Whether to loop back to the beginning when all data is replayed.
    /// </summary>
    public bool LoopReplay { get; set; } = true;
    
    /// <summary>
    /// Readings to include in replay. If empty, all readings are included.
    /// </summary>
    public List<string> IncludedReadings { get; set; } = new();
    
    /// <summary>
    /// Skip NaN (missing/invalid) values instead of publishing them.
    /// Recommended: true (cleaner data for pattern learning).
    /// </summary>
    public bool SkipNaN { get; set; } = true;
    
    /// <summary>
    /// Years of data to load (e.g., [2019, 2020]). 
    /// Files must match pattern: Turbine_Data_Kelmarsh_{num}_{year}-01-01_-_{year+1}-01-01_{id}.csv
    /// </summary>
    public List<int> DataYears { get; set; } = new() { 2019 };
    
    /// <summary>
    /// Kafka topic to publish replay data to.
    /// </summary>
    public string KafkaTopic { get; set; } = "naia.datapoints";
    
    /// <summary>
    /// Enable interpolation to generate intermediate data points between 10-minute intervals.
    /// </summary>
    public bool EnableInterpolation { get; set; } = true;
    
    /// <summary>
    /// Interval in seconds between interpolated data points (default: 15 seconds).
    /// Original data is 10-minute intervals (600 seconds), so 15 seconds = 40 points per interval.
    /// </summary>
    public int InterpolationIntervalSeconds { get; set; } = 15;
}

/// <summary>
/// Configuration for a single reading/measurement type.
/// </summary>
public sealed class ReplayReadingConfig
{
    /// <summary>
    /// Column name in the source CSV file.
    /// </summary>
    public required string CsvColumn { get; set; }
    
    /// <summary>
    /// Suffix for the point name (e.g., "WindSpeed" -> KSH_001_WindSpeed).
    /// </summary>
    public required string PointSuffix { get; set; }
    
    /// <summary>
    /// Engineering units for the reading.
    /// </summary>
    public required string Unit { get; set; }
    
    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }
}
