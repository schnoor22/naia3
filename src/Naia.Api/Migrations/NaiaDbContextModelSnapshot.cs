using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Naia.Infrastructure.Persistence;

#nullable disable

namespace Naia.Api.Migrations
{
    [DbContext(typeof(NaiaDbContext))]
    partial class NaiaDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Naia.Domain.Entities.DataSource", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<string>("ConfigurationJson")
                        .HasColumnType("jsonb")
                        .HasColumnName("configuration_json");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at")
                        .HasDefaultValueSql("now()");

                    b.Property<string>("ConnectionString")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("connection_string");

                    b.Property<string>("Description")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("description");

                    b.Property<bool>("IsEnabled")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasColumnName("is_enabled")
                        .HasDefaultValue(true);

                    b.Property<DateTime?>("LastConnectedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_connected_at");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("name");

                    b.Property<string>("SourceType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("source_type");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("status");

                    b.HasKey("Id");

                    b.HasIndex("IsEnabled");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.HasIndex("SourceType");

                    b.ToTable("data_sources", (string)null);
                });

            modelBuilder.Entity("Naia.Domain.Entities.Point", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("gen_random_uuid()");

                    b.Property<int>("CompressionMaxIntervalSeconds")
                        .HasColumnType("integer")
                        .HasColumnName("compression_max_interval_seconds");

                    b.Property<int>("CompressionMinIntervalSeconds")
                        .HasColumnType("integer")
                        .HasColumnName("compression_min_interval_seconds");

                    b.Property<double>("CompressionDeviation")
                        .HasColumnType("double precision")
                        .HasColumnName("compression_deviation");

                    b.Property<bool>("CompressionEnabled")
                        .HasColumnType("boolean")
                        .HasColumnName("compression_enabled");

                    b.Property<DateTime>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at")
                        .HasDefaultValueSql("now()");

                    b.Property<Guid?>("DataSourceId")
                        .HasColumnType("uuid")
                        .HasColumnName("data_source_id");

                    b.Property<string>("Description")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("description");

                    b.Property<string>("EngineeringUnits")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("engineering_units");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("kind");

                    b.Property<double?>("MaxAcceptableValue")
                        .HasColumnType("double precision")
                        .HasColumnName("max_acceptable_value");

                    b.Property<double?>("MinAcceptableValue")
                        .HasColumnType("double precision")
                        .HasColumnName("min_acceptable_value");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)")
                        .HasColumnName("name");

                    b.Property<long?>("PointSequenceId")
                        .HasColumnType("bigint")
                        .HasColumnName("point_sequence_id")
                        .HasAnnotation("Npgsql:IdentitySequenceOptions", "'1', '1', '', '', 'False', '1'")
                        .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

                    b.Property<bool>("AlertOnOutOfRange")
                        .HasColumnType("boolean")
                        .HasColumnName("alert_on_out_of_range");

                    b.Property<string>("SourceAddress")
                        .HasColumnType("text")
                        .HasColumnName("source_address");

                    b.Property<DateTime>("UpdatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at")
                        .HasDefaultValueSql("now()");

                    b.Property<string>("ValueType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("value_type");

                    b.HasKey("Id");

                    b.HasIndex("DataSourceId");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("points", (string)null);
                });

            modelBuilder.Entity("Naia.Domain.Entities.Point", b =>
                {
                    b.HasOne("Naia.Domain.Entities.DataSource", "DataSource")
                        .WithMany()
                        .HasForeignKey("DataSourceId")
                        .HasConstraintName("FK_points_data_sources_data_source_id");

                    b.Navigation("DataSource");
                });
#pragma warning restore 612, 618
        }
    }
}
