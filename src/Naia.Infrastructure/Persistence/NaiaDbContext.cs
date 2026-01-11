using Microsoft.EntityFrameworkCore;
using Naia.Domain.Entities;

namespace Naia.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL database context for NAIA metadata.
/// </summary>
public class NaiaDbContext : DbContext
{
    public NaiaDbContext(DbContextOptions<NaiaDbContext> options) : base(options)
    {
    }
    
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<Point> Points => Set<Point>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        ConfigureDataSource(modelBuilder);
        ConfigurePoint(modelBuilder);
    }
    
    private static void ConfigureDataSource(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataSource>(entity =>
        {
            entity.ToTable("data_sources");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");
            
            entity.Property(e => e.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(200);
            
            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(1000);
            
            entity.Property(e => e.SourceType)
                .HasColumnName("source_type")
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            
            entity.Property(e => e.ConnectionString)
                .HasColumnName("connection_string")
                .HasMaxLength(2000);
            
            entity.Property(e => e.ConfigurationJson)
                .HasColumnName("configuration_json")
                .HasColumnType("jsonb");
            
            entity.Property(e => e.IsEnabled)
                .HasColumnName("is_enabled")
                .IsRequired()
                .HasDefaultValue(true);
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired()
                .HasDefaultValueSql("now()");
            
            entity.Property(e => e.LastConnectedAt)
                .HasColumnName("last_connected_at");
            
            // Indexes
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.SourceType);
            entity.HasIndex(e => e.IsEnabled);
        });
    }
    
    private static void ConfigurePoint(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Point>(entity =>
        {
            entity.ToTable("points");
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");
            
            entity.Property(e => e.PointSequenceId)
                .HasColumnName("point_sequence_id")
                .ValueGeneratedOnAdd();
            
            entity.Property(e => e.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(500);
            
            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(2000);
            
            entity.Property(e => e.EngineeringUnits)
                .HasColumnName("engineering_units")
                .HasMaxLength(50);
            
            entity.Property(e => e.ValueType)
                .HasColumnName("value_type")
                .HasConversion<string>()
                .HasMaxLength(50);
            
            entity.Property(e => e.Kind)
                .HasColumnName("kind")
                .HasConversion<string>()
                .HasMaxLength(50);
            
            entity.Property(e => e.SourceAddress)
                .HasColumnName("source_address")
                .HasMaxLength(500);
            
            entity.Property(e => e.IsEnabled)
                .HasColumnName("is_enabled")
                .IsRequired();
            
            // Compression settings
            entity.Property(e => e.CompressionEnabled)
                .HasColumnName("compression_enabled")
                .IsRequired();
            
            entity.Property(e => e.CompressionDeviation)
                .HasColumnName("compression_deviation")
                .HasPrecision(18, 6);
            
            entity.Property(e => e.CompressionMinIntervalSeconds)
                .HasColumnName("compression_min_interval_seconds");
            
            entity.Property(e => e.CompressionMaxIntervalSeconds)
                .HasColumnName("compression_max_interval_seconds");
            
            // Exception settings
            entity.Property(e => e.ExceptionEnabled)
                .HasColumnName("exception_enabled")
                .IsRequired();
            
            entity.Property(e => e.ExceptionDeviation)
                .HasColumnName("exception_deviation")
                .HasPrecision(18, 6);
            
            // Alerting
            entity.Property(e => e.AlertOnOutOfRange)
                .HasColumnName("alert_on_out_of_range")
                .IsRequired();
            
            // Scaling
            entity.Property(e => e.Zero)
                .HasColumnName("scale_zero")
                .HasPrecision(18, 6);
            
            entity.Property(e => e.Span)
                .HasColumnName("scale_span")
                .HasPrecision(18, 6);
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired()
                .HasDefaultValueSql("now()");
            
            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired()
                .HasDefaultValueSql("now()");
            
            entity.Property(e => e.LastValueAt)
                .HasColumnName("last_value_at");
            
            entity.Property(e => e.DataSourceId)
                .HasColumnName("data_source_id");
            
            // Foreign key to DataSource
            entity.HasOne(e => e.DataSource)
                .WithMany(ds => ds.Points)
                .HasForeignKey(e => e.DataSourceId)
                .OnDelete(DeleteBehavior.SetNull);
            
            // Indexes
            entity.HasIndex(e => e.PointSequenceId).IsUnique();
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => new { e.DataSourceId, e.Name }).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
        });
    }
}
