using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRawStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceProcessingBatch",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<int>(type: "integer", nullable: true),
                    BatchType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecordsIngested = table.Column<int>(type: "integer", nullable: false),
                    RecordsFailed = table.Column<int>(type: "integer", nullable: false),
                    ErrorSummary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceProcessingBatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceProcessingBatch_AttendanceIntegration_Integration~",
                        column: x => x.IntegrationId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceIntegration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawAttendanceTransaction",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<int>(type: "integer", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<int>(type: "integer", nullable: true),
                    ExternalTransactionId = table.Column<string>(type: "text", nullable: true),
                    ExternalEmployeeId = table.Column<string>(type: "text", nullable: true),
                    TransactionTimestampRaw = table.Column<string>(type: "text", nullable: false),
                    TransactionTimestampParsed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionTypeRaw = table.Column<string>(type: "text", nullable: true),
                    DirectionRaw = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    ApiRequestReference = table.Column<string>(type: "text", nullable: true),
                    RecordReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProcessingError = table.Column<string>(type: "text", nullable: true),
                    RecordHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RetentionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawAttendanceTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawAttendanceTransaction_AttendanceDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawAttendanceTransaction_AttendanceIntegration_IntegrationId",
                        column: x => x.IntegrationId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceIntegration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawAttendanceTransaction_AttendanceProcessingBatch_ImportBa~",
                        column: x => x.ImportBatchId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceProcessingBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProcessingBatch_IntegrationId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_RawAttendanceTransaction_DeviceId",
                schema: "Attendance",
                table: "RawAttendanceTransaction",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_RawAttendanceTransaction_ImportBatchId",
                schema: "Attendance",
                table: "RawAttendanceTransaction",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RawAttendanceTransaction_IntegrationId",
                schema: "Attendance",
                table: "RawAttendanceTransaction",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_RawAttendanceTransaction_TenantId_ImportBatchId_ProcessingS~",
                schema: "Attendance",
                table: "RawAttendanceTransaction",
                columns: new[] { "TenantId", "ImportBatchId", "ProcessingStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RawAttendanceTransaction_TenantId_RecordHash",
                schema: "Attendance",
                table: "RawAttendanceTransaction",
                columns: new[] { "TenantId", "RecordHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawAttendanceTransaction",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceProcessingBatch",
                schema: "Attendance");
        }
    }
}
