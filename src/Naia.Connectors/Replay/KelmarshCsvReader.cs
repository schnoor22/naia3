using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Naia.Connectors.Replay;

/// <summary>
/// Reads Kelmarsh wind turbine CSV data files.
/// 
/// Kelmarsh CSV format:
/// - First 9 rows are metadata (site info, time range, etc.)
/// - Row 10 (index 9) is the header row
/// - Data starts at row 11
/// - 10-minute intervals
/// - NaN for missing values
/// </summary>
public sealed class KelmarshCsvReader : IDisposable
{
    private readonly ILogger<KelmarshCsvReader> _logger;
    private readonly ReplayOptions _options;
    
    // Kelmarsh CSV has 9 metadata rows before the header
    private const int HeaderRowIndex = 9;
    
    // Standard readings available in Kelmarsh data
    public static readonly List<ReplayReadingConfig> StandardReadings = new()
    {
        new() { CsvColumn = "Wind speed (m/s)", PointSuffix = "WindSpeed", Unit = "m/s", Description = "Nacelle anemometer wind speed" },
        new() { CsvColumn = "Power (kW)", PointSuffix = "Power", Unit = "kW", Description = "Active power output" },
        new() { CsvColumn = "Wind direction (°)", PointSuffix = "WindDirection", Unit = "deg", Description = "Wind direction from nacelle" },
        new() { CsvColumn = "Nacelle position (°)", PointSuffix = "NacellePosition", Unit = "deg", Description = "Nacelle yaw position" },
        new() { CsvColumn = "Rotor speed (RPM)", PointSuffix = "RotorRPM", Unit = "RPM", Description = "Rotor rotational speed" },
        new() { CsvColumn = "Generator RPM (RPM)", PointSuffix = "GeneratorRPM", Unit = "RPM", Description = "Generator rotational speed" },
        new() { CsvColumn = "Blade angle (pitch position) A (°)", PointSuffix = "PitchA", Unit = "deg", Description = "Blade A pitch angle" },
        new() { CsvColumn = "Blade angle (pitch position) B (°)", PointSuffix = "PitchB", Unit = "deg", Description = "Blade B pitch angle" },
        new() { CsvColumn = "Blade angle (pitch position) C (°)", PointSuffix = "PitchC", Unit = "deg", Description = "Blade C pitch angle" },
        new() { CsvColumn = "Nacelle temperature (°C)", PointSuffix = "NacelleTemp", Unit = "C", Description = "Nacelle internal temperature" },
        new() { CsvColumn = "Gear oil temperature (°C)", PointSuffix = "GearOilTemp", Unit = "C", Description = "Gearbox oil temperature" },
        new() { CsvColumn = "Generator bearing front temperature (°C)", PointSuffix = "GenBearingFrontTemp", Unit = "C", Description = "Generator front bearing temperature" },
        new() { CsvColumn = "Generator bearing rear temperature (°C)", PointSuffix = "GenBearingRearTemp", Unit = "C", Description = "Generator rear bearing temperature" },
        new() { CsvColumn = "Ambient temperature (converter) (°C)", PointSuffix = "AmbientTemp", Unit = "C", Description = "Ambient temperature at converter" },
        new() { CsvColumn = "Grid voltage (V)", PointSuffix = "GridVoltage", Unit = "V", Description = "Grid voltage" },
        new() { CsvColumn = "Grid frequency (Hz)", PointSuffix = "GridFrequency", Unit = "Hz", Description = "Grid frequency" },
        new() { CsvColumn = "Reactive power (kvar)", PointSuffix = "ReactivePower", Unit = "kvar", Description = "Reactive power" },
        new() { CsvColumn = "Energy Export (kWh)", PointSuffix = "EnergyExport", Unit = "kWh", Description = "Energy exported this interval" },
    };
    
    // Column indices cache
    private Dictionary<int, Dictionary<string, int>> _columnIndices = new();
    
    public KelmarshCsvReader(
        IOptions<ReplayOptions> options,
        ILogger<KelmarshCsvReader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }
    
    /// <summary>
    /// Get all available CSV files for all turbines across configured years.
    /// </summary>
    public IEnumerable<(int TurbineNumber, string FilePath)> GetTurbineFiles()
    {
        var dataDir = _options.DataDirectory;
        
        if (!Directory.Exists(dataDir))
        {
            _logger.LogWarning("Data directory not found: {DataDir}", dataDir);
            yield break;
        }
        
        // Support multiple years of data if configured
        var years = _options.DataYears.Count > 0 ? _options.DataYears.Distinct().ToList() : new List<int> { 2019 };
        
        _logger.LogInformation("Looking for turbine data files in {DataDir} for years: {Years}", 
            dataDir, string.Join(", ", years));
        
        var foundFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Look for Kelmarsh turbine files with pattern: Turbine_Data_Kelmarsh_{num}_{year}-01-01_-_{year+1}-01-01_{id}.csv
        for (int turbine = 1; turbine <= _options.TurbineCount; turbine++)
        {
            bool foundForTurbine = false;
            foreach (var year in years)
            {
                if (foundForTurbine) break; // Already found file for this turbine
                
                // Try different common patterns
                var patterns = new[]
                {
                    $"*Kelmarsh_{turbine}_{year}-01-01_-_{year + 1}-01-01*.csv",
                    $"*Turbine*Kelmarsh*{turbine}*{year}*.csv",
                    $"*Kelmarsh*{turbine}*{year}*.csv"
                };
                
                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(dataDir, pattern, SearchOption.AllDirectories)
                        .Where(f => f.Contains("Turbine_Data", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    
                    if (files.Length > 0)
                    {
                        // Use the first matching file that we haven't seen yet
                        var file = files[0];
                        if (foundFiles.Add(file))
                        {
                            yield return (turbine, file);
                            _logger.LogDebug("Found turbine {Num} year {Year} data: {File}", 
                                turbine, year, Path.GetFileName(file));
                            foundForTurbine = true;
                        }
                        break; // Found a match, skip other patterns
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Read data from a turbine CSV file, yielding batches of data points.
    /// </summary>
    public IEnumerable<List<ReplayDataPoint>> ReadTurbineDataBatched(
        int turbineNumber,
        string filePath,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int batchSize = 100)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found: {FilePath}", filePath);
            yield break;
        }
        
        var readings = GetActiveReadings();
        var batch = new List<ReplayDataPoint>(batchSize * readings.Count);
        
        using var reader = new StreamReader(filePath);
        
        // Skip metadata rows and read header
        string? headerLine = null;
        for (int i = 0; i <= HeaderRowIndex; i++)
        {
            headerLine = reader.ReadLine();
        }
        
        if (string.IsNullOrEmpty(headerLine))
        {
            _logger.LogError("Could not read header from {FilePath}", filePath);
            yield break;
        }
        
        // Parse header to get column indices
        var headers = ParseCsvLine(headerLine);
        var columnIndices = BuildColumnIndices(headers, readings);
        
        // Find timestamp column (usually "Date and time" or similar)
        int timestampIndex = FindTimestampColumn(headers);
        if (timestampIndex < 0)
        {
            _logger.LogError("Could not find timestamp column in {FilePath}", filePath);
            yield break;
        }
        
        // Read data rows
        string? line;
        int rowsRead = 0;
        
        while ((line = reader.ReadLine()) != null)
        {
            var columns = ParseCsvLine(line);
            if (columns.Length <= timestampIndex)
                continue;
            
            // Parse timestamp
            if (!DateTime.TryParse(columns[timestampIndex], CultureInfo.InvariantCulture, 
                DateTimeStyles.AssumeUniversal, out var timestamp))
            {
                continue;
            }
            
            // Apply time filters
            if (startTime.HasValue && timestamp < startTime.Value)
                continue;
            if (endTime.HasValue && timestamp > endTime.Value)
                continue;
            
            // Extract readings for this row
            foreach (var reading in readings)
            {
                if (!columnIndices.TryGetValue(reading.PointSuffix, out var colIndex))
                    continue;
                
                if (colIndex >= columns.Length)
                    continue;
                
                var valueStr = columns[colIndex];
                
                // Skip NaN values if configured
                if (_options.SkipNaN &&
                    (string.IsNullOrWhiteSpace(valueStr) || 
                     valueStr.Equals("NaN", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                
                if (!double.TryParse(valueStr, CultureInfo.InvariantCulture, out var value))
                    continue;
                
                // Create point name: {SiteCode}_{TurbineNum:000}_{PointSuffix}
                var pointName = $"{_options.SiteCode}_{turbineNumber:D3}_{reading.PointSuffix}";
                
                batch.Add(new ReplayDataPoint
                {
                    PointName = pointName,
                    SourceAddress = $"replay://{_options.SiteCode}/turbine{turbineNumber}/{reading.PointSuffix}",
                    OriginalTimestamp = timestamp,
                    Value = value,
                    Unit = reading.Unit,
                    TurbineNumber = turbineNumber,
                    ReadingType = reading.PointSuffix
                });
            }
            
            rowsRead++;
            
            // Yield batch when full
            if (batch.Count >= batchSize)
            {
                yield return batch;
                batch = new List<ReplayDataPoint>(batchSize * readings.Count);
            }
        }
        
        // Yield remaining
        if (batch.Count > 0)
        {
            yield return batch;
        }
        
        _logger.LogInformation("Read {Rows} rows from turbine {Num}", rowsRead, turbineNumber);
    }
    
    /// <summary>
    /// Get all data points for a specific timestamp across all turbines.
    /// </summary>
    public async Task<List<ReplayDataPoint>> GetDataPointsForTimestampAsync(
        DateTime targetTimestamp,
        CancellationToken ct = default)
    {
        var results = new List<ReplayDataPoint>();
        
        foreach (var (turbineNum, filePath) in GetTurbineFiles())
        {
            foreach (var batch in ReadTurbineDataBatched(turbineNum, filePath,
                startTime: targetTimestamp.AddMinutes(-5),
                endTime: targetTimestamp.AddMinutes(5),
                batchSize: 1000))
            {
                // Find closest match to target timestamp
                var closest = batch
                    .Where(p => Math.Abs((p.OriginalTimestamp - targetTimestamp).TotalMinutes) < 5)
                    .ToList();
                
                results.AddRange(closest);
            }
        }
        
        return results;
    }
    
    /// <summary>
    /// Get the time range of available data.
    /// </summary>
    public (DateTime Start, DateTime End)? GetDataTimeRange()
    {
        DateTime? minTime = null;
        DateTime? maxTime = null;
        
        foreach (var (turbineNum, filePath) in GetTurbineFiles())
        {
            using var reader = new StreamReader(filePath);
            
            // Skip to header
            for (int i = 0; i <= HeaderRowIndex; i++)
                reader.ReadLine();
            
            var headerLine = reader.ReadLine();
            if (string.IsNullOrEmpty(headerLine))
                continue;
            
            var headers = ParseCsvLine(headerLine);
            int timestampIndex = FindTimestampColumn(headers);
            if (timestampIndex < 0)
                continue;
            
            // Read first data row for start time
            var firstLine = reader.ReadLine();
            if (!string.IsNullOrEmpty(firstLine))
            {
                var cols = ParseCsvLine(firstLine);
                if (cols.Length > timestampIndex &&
                    DateTime.TryParse(cols[timestampIndex], CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var startTs))
                {
                    if (!minTime.HasValue || startTs < minTime.Value)
                        minTime = startTs;
                }
            }
            
            // Read last lines for end time (read file to end)
            string? lastLine = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lastLine = line;
            }
            
            if (!string.IsNullOrEmpty(lastLine))
            {
                var cols = ParseCsvLine(lastLine);
                if (cols.Length > timestampIndex &&
                    DateTime.TryParse(cols[timestampIndex], CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var endTs))
                {
                    if (!maxTime.HasValue || endTs > maxTime.Value)
                        maxTime = endTs;
                }
            }
        }
        
        if (minTime.HasValue && maxTime.HasValue)
        {
            return (minTime.Value, maxTime.Value);
        }
        
        return null;
    }
    
    private List<ReplayReadingConfig> GetActiveReadings()
    {
        if (_options.IncludedReadings.Count == 0)
        {
            return StandardReadings;
        }
        
        return StandardReadings
            .Where(r => _options.IncludedReadings.Contains(r.PointSuffix))
            .ToList();
    }
    
    private static string[] ParseCsvLine(string line)
    {
        // Handle quoted CSV fields
        var result = new List<string>();
        var inQuotes = false;
        var current = new System.Text.StringBuilder();
        
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }
    
    private Dictionary<string, int> BuildColumnIndices(string[] headers, List<ReplayReadingConfig> readings)
    {
        var indices = new Dictionary<string, int>();
        
        // Clean headers (remove # prefix if present)
        var cleanHeaders = headers.Select(h => h.TrimStart('#', ' ')).ToArray();
        
        foreach (var reading in readings)
        {
            // Try exact match first
            var index = Array.FindIndex(cleanHeaders, h => 
                h.Equals(reading.CsvColumn, StringComparison.OrdinalIgnoreCase));
            
            // Try partial match (handle encoding issues with degree symbols)
            if (index < 0)
            {
                // Replace degree symbol variations
                var normalizedColumn = reading.CsvColumn
                    .Replace("°", "")
                    .Replace("\u00b0", "")
                    .Trim();
                
                index = Array.FindIndex(cleanHeaders, h =>
                {
                    var normalizedHeader = h.Replace("°", "").Replace("\u00b0", "").Replace("Â°", "").Trim();
                    return normalizedHeader.Contains(normalizedColumn, StringComparison.OrdinalIgnoreCase) ||
                           normalizedColumn.Contains(normalizedHeader, StringComparison.OrdinalIgnoreCase);
                });
            }
            
            if (index >= 0)
            {
                indices[reading.PointSuffix] = index;
            }
            else
            {
                _logger.LogDebug("Column not found: {Column}", reading.CsvColumn);
            }
        }
        
        return indices;
    }
    
    private static int FindTimestampColumn(string[] headers)
    {
        var cleanHeaders = headers.Select(h => h.TrimStart('#', ' ')).ToArray();
        
        // Try common timestamp column names
        var timestampNames = new[] { "Date and time", "Timestamp", "DateTime", "Time" };
        
        foreach (var name in timestampNames)
        {
            var index = Array.FindIndex(cleanHeaders, h =>
                h.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            if (index >= 0)
                return index;
        }
        
        // Default to first column
        return 0;
    }
    
    public void Dispose()
    {
        // Nothing to dispose currently
    }
}

/// <summary>
/// Represents a single data point from the replay data.
/// </summary>
public sealed class ReplayDataPoint
{
    /// <summary>
    /// Full point name (e.g., "KSH_001_WindSpeed").
    /// </summary>
    public required string PointName { get; init; }
    
    /// <summary>
    /// Source address (e.g., "replay://KSH/turbine1/WindSpeed").
    /// </summary>
    public required string SourceAddress { get; init; }
    
    /// <summary>
    /// Original timestamp from the CSV data.
    /// </summary>
    public DateTime OriginalTimestamp { get; init; }
    
    /// <summary>
    /// Adjusted timestamp (after applying offset to simulate current time).
    /// </summary>
    public DateTime AdjustedTimestamp { get; set; }
    
    /// <summary>
    /// The measured value.
    /// </summary>
    public double Value { get; init; }
    
    /// <summary>
    /// Engineering units.
    /// </summary>
    public required string Unit { get; init; }
    
    /// <summary>
    /// Turbine number (1-6).
    /// </summary>
    public int TurbineNumber { get; init; }
    
    /// <summary>
    /// Reading type suffix (e.g., "WindSpeed", "Power").
    /// </summary>
    public required string ReadingType { get; init; }
}
