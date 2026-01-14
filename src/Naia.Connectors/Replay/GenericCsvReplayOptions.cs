namespace Naia.Connectors.Replay;

/// <summary>
/// Configuration for generic CSV replay from multiple industrial sites.
/// Supports various CSV formats with standardized timestamp/value/status columns.
/// </summary>
public sealed class GenericCsvReplayOptions
{
    public const string SectionName = "GenericCsvReplay";
    
    /// <summary>
    /// Enable/disable the generic CSV replay connector.
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Site configurations for replay.
    /// </summary>
    public List<SiteReplayConfig> Sites { get; set; } = new();
    
    /// <summary>
    /// Kafka topic to publish data to.
    /// </summary>
    public string KafkaTopic { get; set; } = "naia.datapoints";
    
    /// <summary>
    /// Maximum batch size for Kafka publishing.
    /// </summary>
    public int BatchSize { get; set; } = 500;
    
    /// <summary>
    /// Whether to loop replay when data ends (useful for continuous testing).
    /// </summary>
    public bool LoopReplay { get; set; } = true;
    
    /// <summary>
    /// Speed multiplier. 1.0 = real-time intervals, 0 = as fast as possible.
    /// </summary>
    public double SpeedMultiplier { get; set; } = 0.0; // Default: fast load
    
    /// <summary>
    /// How to handle bad status values: "Skip", "Store", "ConvertToNull"
    /// </summary>
    public string BadStatusHandling { get; set; } = "Store";
    
    // ============================================================================
    // RATE LIMITING (Commented out - enable when needed for high-volume sites)
    // ============================================================================
    // /// <summary>
    // /// Maximum points per second to publish. 0 = unlimited.
    // /// Use to prevent overwhelming downstream consumers.
    // /// </summary>
    // public int MaxPointsPerSecond { get; set; } = 0;
    // 
    // /// <summary>
    // /// Delay in milliseconds between batches. Helps distribute load.
    // /// </summary>
    // public int BatchDelayMs { get; set; } = 0;
}

/// <summary>
/// Configuration for a single site's data replay.
/// </summary>
public sealed class SiteReplayConfig
{
    /// <summary>
    /// Unique site identifier (used as DataSourceId).
    /// </summary>
    public string SiteId { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable site name.
    /// </summary>
    public string SiteName { get; set; } = string.Empty;
    
    /// <summary>
    /// Directory containing CSV files for this site.
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;
    
    /// <summary>
    /// IANA timezone identifier for the site (e.g., "America/Chicago").
    /// CRITICAL: CSV timestamps are assumed to be in this local timezone.
    /// </summary>
    public string Timezone { get; set; } = "UTC";
    
    /// <summary>
    /// Tag name prefix to add (e.g., "SITE1_"). Empty = no prefix.
    /// </summary>
    public string TagPrefix { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of characters to strip from the original tag name (from start).
    /// Useful for removing vendor/site prefixes like "MSR1SINV11A01" → "01".
    /// </summary>
    public int StripPrefixLength { get; set; } = 0;
    
    /// <summary>
    /// Offset in seconds to delay this site's data publishing.
    /// Use to stagger multiple sites and prevent thundering herd.
    /// Example: Site1=0, Site2=30, Site3=60 spreads load evenly.
    /// </summary>
    public int StartOffsetSeconds { get; set; } = 0;
    
    /// <summary>
    /// CSV format configuration.
    /// </summary>
    public CsvFormatConfig CsvFormat { get; set; } = new();
}

/// <summary>
/// CSV file format configuration.
/// </summary>
public sealed class CsvFormatConfig
{
    /// <summary>
    /// Column name or index for timestamp (0-based if integer).
    /// </summary>
    public string TimestampColumn { get; set; } = "Timestamp";
    
    /// <summary>
    /// Column name or index for value.
    /// </summary>
    public string ValueColumn { get; set; } = "Value";
    
    /// <summary>
    /// Column name or index for status/quality (optional).
    /// </summary>
    public string? StatusColumn { get; set; } = "Status";
    
    /// <summary>
    /// Timestamp format string (for DateTime.ParseExact).
    /// Leave empty for automatic parsing.
    /// </summary>
    public string? TimestampFormat { get; set; }
    
    /// <summary>
    /// Header row index (0-based). 0 = first row is header.
    /// </summary>
    public int HeaderRow { get; set; } = 0;
    
    /// <summary>
    /// Number of rows to skip before header (for files with metadata).
    /// </summary>
    public int SkipRowsBeforeHeader { get; set; } = 0;
    
    /// <summary>
    /// CSV delimiter character.
    /// </summary>
    public char Delimiter { get; set; } = ',';
    
    /// <summary>
    /// How to extract tag name: "Filename", "ColumnValue", "Fixed"
    /// </summary>
    public string TagNameSource { get; set; } = "Filename";
    
    /// <summary>
    /// Regex pattern to extract tag from filename (if TagNameSource = "Filename").
    /// Example: "^(.*?)_\\d{8}_\\d{6}\\.csv$" extracts tag from "TAGNAME_20260112_222703.csv"
    /// </summary>
    public string? FilenameTagPattern { get; set; }
    
    /// <summary>
    /// Column name for tag (if TagNameSource = "ColumnValue").
    /// </summary>
    public string? TagColumn { get; set; }
    
    /// <summary>
    /// Fixed tag name (if TagNameSource = "Fixed").
    /// </summary>
    public string? FixedTagName { get; set; }
}
