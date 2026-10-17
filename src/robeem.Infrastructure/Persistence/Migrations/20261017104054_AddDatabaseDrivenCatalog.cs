using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseDrivenCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultFileFormatId",
                schema: "Attendance",
                table: "AttendanceProvider",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadColumns",
                schema: "Attendance",
                table: "AttendanceFileFormat",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceDeviceType",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDeviceType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceTransportDefinition",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransportKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsPull = table.Column<bool>(type: "boolean", nullable: false),
                    UsesFileFormat = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceTransportDefinition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceProviderTransport",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<int>(type: "integer", nullable: false),
                    TransportId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceProviderTransport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceProviderTransport_AttendanceProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceProvider",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceProviderTransport_AttendanceTransportDefinition_T~",
                        column: x => x.TransportId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceTransportDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProvider_DefaultFileFormatId",
                schema: "Attendance",
                table: "AttendanceProvider",
                column: "DefaultFileFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevice_DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice",
                column: "DeviceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceType_TenantId_Name",
                schema: "Attendance",
                table: "AttendanceDeviceType",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProviderTransport_ProviderId",
                schema: "Attendance",
                table: "AttendanceProviderTransport",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProviderTransport_TenantId_ProviderId_TransportId",
                schema: "Attendance",
                table: "AttendanceProviderTransport",
                columns: new[] { "TenantId", "ProviderId", "TransportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProviderTransport_TransportId",
                schema: "Attendance",
                table: "AttendanceProviderTransport",
                column: "TransportId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceTransportDefinition_TenantId_TransportKey",
                schema: "Attendance",
                table: "AttendanceTransportDefinition",
                columns: new[] { "TenantId", "TransportKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceDevice_AttendanceDeviceType_DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice",
                column: "DeviceTypeId",
                principalSchema: "Attendance",
                principalTable: "AttendanceDeviceType",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceProvider_AttendanceFileFormat_DefaultFileFormatId",
                schema: "Attendance",
                table: "AttendanceProvider",
                column: "DefaultFileFormatId",
                principalSchema: "Attendance",
                principalTable: "AttendanceFileFormat",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceDevice_AttendanceDeviceType_DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceProvider_AttendanceFileFormat_DefaultFileFormatId",
                schema: "Attendance",
                table: "AttendanceProvider");

            migrationBuilder.DropTable(
                name: "AttendanceDeviceType",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceProviderTransport",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceTransportDefinition",
                schema: "Attendance");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceProvider_DefaultFileFormatId",
                schema: "Attendance",
                table: "AttendanceProvider");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceDevice_DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropColumn(
                name: "DefaultFileFormatId",
                schema: "Attendance",
                table: "AttendanceProvider");

            migrationBuilder.DropColumn(
                name: "PayloadColumns",
                schema: "Attendance",
                table: "AttendanceFileFormat");

            migrationBuilder.DropColumn(
                name: "DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice");
        }
    }
}
