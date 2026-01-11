using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Naia.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    connection_string = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_connected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    point_sequence_id = table.Column<long>(type: "bigint", nullable: true)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    engineering_units = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    value_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    compression_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    compression_deviation = table.Column<double>(type: "numeric(18,6)", nullable: false),
                    compression_min_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    compression_max_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    exception_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    exception_deviation = table.Column<double>(type: "numeric(18,6)", nullable: false),
                    scale_zero = table.Column<double>(type: "numeric(18,6)", nullable: false),
                    scale_span = table.Column<double>(type: "numeric(18,6)", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_value_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_points", x => x.id);
                    table.ForeignKey(
                        name: "FK_points_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_sources_is_enabled",
                table: "data_sources",
                column: "is_enabled");

            migrationBuilder.CreateIndex(
                name: "IX_data_sources_name",
                table: "data_sources",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_data_sources_source_type",
                table: "data_sources",
                column: "source_type");

            migrationBuilder.CreateIndex(
                name: "IX_points_data_source_id",
                table: "points",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_points_name",
                table: "points",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_points_sequence_id",
                table: "points",
                column: "point_sequence_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_points_datasource_name",
                table: "points",
                columns: new[] { "data_source_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_points_is_enabled",
                table: "points",
                column: "is_enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "points");

            migrationBuilder.DropTable(
                name: "data_sources");
        }
    }
}
