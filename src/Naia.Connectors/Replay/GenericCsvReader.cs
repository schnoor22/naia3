using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Naia.Connectors.Replay;

/// <summary>
/// Reads generic CSV files from industrial sites with flexible format support.
/// Handles timestamp/value/status format with configurable columns and preprocessing.
/// </summary>
public sealed class GenericCsvReader
{
    private readonly ILogger<GenericCsvReader> _logger;
    
    public GenericCsvReader(ILogger<GenericCsvReader> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Scan a site directory and return all CSV files with their extracted tag names.
    /// </summary>
    public IEnumerable<(string TagName, string FilePath)> ScanSiteFiles(SiteReplayConfig site)
    {
        if (!Directory.Exists(site.DataDirectory))
        {
            _logger.LogWarning("Site directory not found: {Dir}", site.DataDirectory);
            yield break;
        }
        
        var files = Directory.GetFiles(site.DataDirectory, "*.csv", SearchOption.AllDirectories);
        _logger.LogInformation("Found {Count} CSV files in {Dir}", files.Length, site.DataDirectory);
        
        foreach (var file in files)
        {
            var tagName = ExtractTagName(file, site);
            if (!string.IsNullOrEmpty(tagName))
            {
                yield return (tagName, file);
            }
        }
    }
    
    /// <summary>
    /// Extract tag name from filename based on site configuration.
    /// </summary>
    private string ExtractTagName(string filePath, SiteReplayConfig site)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var format = site.CsvFormat;
        
        string originalTag = format.TagNameSource switch
        {
            "Filename" => ExtractTagFromFilename(fileName, format.FilenameTagPattern),
            "Fixed" => format.FixedTagName ?? fileName,
            "ColumnValue" => fileName, // Will be read from CSV in ReadFile
            _ => fileName
        };
        
        // Apply prefix stripping
        if (site.StripPrefixLength > 0 && originalTag.Length > site.StripPrefixLength)
        {
            originalTag = originalTag.Substring(site.StripPrefixLength);
        }
        
        // Apply site prefix
        if (!string.IsNullOrEmpty(site.TagPrefix))
        {
            originalTag = site.TagPrefix + originalTag;
        }
        
        return originalTag;
    }
    
    /// <summary>
    /// Extract tag from filename using regex pattern.
    /// </summary>
    private string ExtractTagFromFilename(string fileName, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            // Default: Use entire filename
            return fileName;
        }
        
        try
        {
            var match = Regex.Match(fileName, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply regex pattern to filename: {Pattern}", pattern);
        }
        
        return fileName;
    }
    
    /// <summary>
    /// Read all data from a CSV file.
    /// </summary>
    public IEnumerable<CsvDataPoint> ReadFile(
        string filePath, 
        string tagName,
        SiteReplayConfig site)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found: {Path}", filePath);
            yield break;
        }
        
        var format = site.CsvFormat;
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(site.Timezone);
        
        using var reader = new StreamReader(filePath);
        
        // Skip metadata rows if configured
        for (int i = 0; i < format.SkipRowsBeforeHeader; i++)
        {
            reader.ReadLine();
        }
        
        // Read header
        var headerLine = reader.ReadLine();
        if (string.IsNullOrEmpty(headerLine))
        {
            _logger.LogError("Empty header in file: {Path}", filePath);
            yield break;
        }
        
        var headers = headerLine.Split(format.Delimiter);
        var columnMap = BuildColumnMap(headers, format);
        
        if (!columnMap.ContainsKey("timestamp") || !columnMap.ContainsKey("value"))
        {
            _logger.LogError("Could not find required columns in {Path}. Headers: {Headers}", 
                filePath, string.Join(", ", headers));
            yield break;
        }
        
        int lineNumber = format.SkipRowsBeforeHeader + 1;
        
        // Read data rows
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            lineNumber++;
            
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            var values = line.Split(format.Delimiter);
            
            if (values.Length <= columnMap["timestamp"] || values.Length <= columnMap["value"])
            {
                _logger.LogWarning("Malformed row at line {Line} in {Path}", lineNumber, filePath);
                continue;
            }
            
            // Parse timestamp (local time)
            DateTime localTime;
            try
            {
                var timestampStr = values[columnMap["timestamp"]].Trim();
                
                if (!string.IsNullOrEmpty(format.TimestampFormat))
                {
                    localTime = DateTime.ParseExact(
                        timestampStr, 
                        format.TimestampFormat, 
                        CultureInfo.InvariantCulture);
                }
                else
                {
                    localTime = DateTime.Parse(timestampStr, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse timestamp at line {Line} in {Path}: {Value}", 
                    lineNumber, filePath, values[columnMap["timestamp"]]);
                continue;
            }
            
            // Convert to UTC
            DateTime utcTime;
            try
            {
                utcTime = TimeZoneInfo.ConvertTimeToUtc(localTime, timezone);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert time to UTC at line {Line}: {Time}", 
                    lineNumber, localTime);
                continue;
            }
            
            // Parse value
            if (!double.TryParse(values[columnMap["value"]].Trim(), NumberStyles.Float, 
                CultureInfo.InvariantCulture, out var value))
            {
                _logger.LogDebug("Could not parse value at line {Line}: {Value}", 
                    lineNumber, values[columnMap["value"]]);
                continue;
            }
            
            // Parse status if available
            string status = "Good";
            if (columnMap.ContainsKey("status") && 
                columnMap["status"] < values.Length && 
                !string.IsNullOrWhiteSpace(values[columnMap["status"]]))
            {
                status = values[columnMap["status"]].Trim();
            }
            
            yield return new CsvDataPoint
            {
                TagName = tagName,
                SourceTag = $"csv://{site.SiteId}/{tagName}",
                Timestamp = utcTime,
                Value = value,
                Status = status
            };
        }
    }
    
    /// <summary>
    /// Build a map of logical column names to indices.
    /// </summary>
    private Dictionary<string, int> BuildColumnMap(string[] headers, CsvFormatConfig format)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        // Try to find columns by name or use index
        map["timestamp"] = FindColumnIndex(headers, format.TimestampColumn);
        map["value"] = FindColumnIndex(headers, format.ValueColumn);
        
        if (!string.IsNullOrEmpty(format.StatusColumn))
        {
            var statusIdx = FindColumnIndex(headers, format.StatusColumn);
            if (statusIdx >= 0)
            {
                map["status"] = statusIdx;
            }
        }
        
        if (!string.IsNullOrEmpty(format.TagColumn))
        {
            var tagIdx = FindColumnIndex(headers, format.TagColumn);
            if (tagIdx >= 0)
            {
                map["tag"] = tagIdx;
            }
        }
        
        return map;
    }
    
    /// <summary>
    /// Find column index by name or parse as integer index.
    /// </summary>
    private int FindColumnIndex(string[] headers, string columnSpec)
    {
        // Try as integer index first
        if (int.TryParse(columnSpec, out var index))
        {
            return index;
        }
        
        // Search by name (case-insensitive)
        for (int i = 0; i < headers.Length; i++)
        {
            if (headers[i].Equals(columnSpec, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        
        return -1;
    }
}

/// <summary>
/// Represents a single data point read from CSV.
/// </summary>
public sealed class CsvDataPoint
{
    public string TagName { get; set; } = string.Empty;
    public string SourceTag { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
    public string Status { get; set; } = "Good";
}
