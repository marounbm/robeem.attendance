using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Attendance");

            migrationBuilder.CreateTable(
                name: "AttendancePolicy",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TimeScheduleId = table.Column<int>(type: "integer", nullable: true),
                    RoundingRuleMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    EarlyDepartureGraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    OvertimeThresholdMinutes = table.Column<int>(type: "integer", nullable: false),
                    RequiresOvertimePreApproval = table.Column<bool>(type: "boolean", nullable: false),
                    CorrectionApprovalWorkflow = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UnlabeledPunchInterpretationMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DuplicateTimeWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePolicy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendancePolicy_TimeSchedule_TimeScheduleId",
                        column: x => x.TimeScheduleId,
                        principalSchema: "HR",
                        principalTable: "TimeSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceProvider",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SupportedTransports = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SupportsWebhooks = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsIncrementalSync = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceProvider", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceIntegration",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ConnectionConfigRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PollingScheduleCron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SyncCursor = table.Column<string>(type: "text", nullable: true),
                    LastSuccessfulSyncAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    HealthStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceIntegration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceIntegration_AttendanceProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceProvider",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceDevice",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<int>(type: "integer", nullable: false),
                    ExternalDeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDevice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceDevice_AttendanceIntegration_IntegrationId",
                        column: x => x.IntegrationId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceIntegration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevice_IntegrationId",
                schema: "Attendance",
                table: "AttendanceDevice",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevice_TenantId_IntegrationId_ExternalDeviceId",
                schema: "Attendance",
                table: "AttendanceDevice",
                columns: new[] { "TenantId", "IntegrationId", "ExternalDeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceIntegration_ProviderId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicy_TenantId",
                schema: "Attendance",
                table: "AttendancePolicy",
                column: "TenantId",
                unique: true,
                filter: "\"TimeScheduleId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicy_TenantId_TimeScheduleId",
                schema: "Attendance",
                table: "AttendancePolicy",
                columns: new[] { "TenantId", "TimeScheduleId" },
                unique: true,
                filter: "\"TimeScheduleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicy_TimeScheduleId",
                schema: "Attendance",
                table: "AttendancePolicy",
                column: "TimeScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProvider_TenantId_ProviderKey",
                schema: "Attendance",
                table: "AttendanceProvider",
                columns: new[] { "TenantId", "ProviderKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceDevice",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendancePolicy",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceIntegration",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceProvider",
                schema: "Attendance");
        }
    }
}
