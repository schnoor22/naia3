namespace Naia.Api.Dtos;

/// <summary>
/// DTO for Point responses - avoids circular references with DataSource
/// </summary>
public record PointDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? SourceAddress { get; init; }
    public string? Description { get; init; }
    public string? EngineeringUnits { get; init; }
    public string ValueType { get; init; } = "Float64";
    public string Kind { get; init; } = "Input";
    public bool IsEnabled { get; init; }
    public DateTime? CreatedAt { get; init; }
    public Guid? DataSourceId { get; init; }
    public string? DataSourceName { get; init; }
    
    // Compression
    public bool CompressionEnabled { get; init; }
    public double CompressionDeviation { get; init; }
    
    // Exception
    public bool ExceptionEnabled { get; init; }
    public double ExceptionDeviation { get; init; }
    
    // Scaling
    public double ScaleZero { get; init; }
    public double ScaleSpan { get; init; }
}

/// <summary>Extension methods to convert Point entities to DTOs</summary>
public static class PointDtoExtensions
{
    public static PointDto ToDto(this Naia.Domain.Entities.Point point)
    {
        return new PointDto
        {
            Id = point.Id,
            Name = point.Name,
            SourceAddress = point.SourceAddress,
            Description = point.Description,
            EngineeringUnits = point.EngineeringUnits,
            ValueType = point.ValueType.ToString(),
            Kind = point.Kind.ToString(),
            IsEnabled = point.IsEnabled,
            CreatedAt = point.CreatedAt,
            DataSourceId = point.DataSourceId,
            DataSourceName = point.DataSource?.Name,
            CompressionEnabled = point.CompressionEnabled,
            CompressionDeviation = point.CompressionDeviation,
            ExceptionEnabled = point.ExceptionEnabled,
            ExceptionDeviation = point.ExceptionDeviation,
            ScaleZero = point.Zero,
            ScaleSpan = point.Span
        };
    }
}
