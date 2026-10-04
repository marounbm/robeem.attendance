using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedAttendanceTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NormalizedAttendanceTransaction",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: true),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EventTimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventTimestampLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedEventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalEventType = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<int>(type: "integer", nullable: true),
                    IntegrationId = table.Column<int>(type: "integer", nullable: false),
                    RawTransactionId = table.Column<int>(type: "integer", nullable: false),
                    EventSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfidenceStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DuplicateStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DuplicateOfTransactionId = table.Column<int>(type: "integer", nullable: true),
                    ExceptionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DuplicateBucketTimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedAttendanceTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedAttendanceTransaction_AttendanceDevice_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NormalizedAttendanceTransaction_AttendanceIntegration_Integ~",
                        column: x => x.IntegrationId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceIntegration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NormalizedAttendanceTransaction_NormalizedAttendanceTransac~",
                        column: x => x.DuplicateOfTransactionId,
                        principalSchema: "Attendance",
                        principalTable: "NormalizedAttendanceTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NormalizedAttendanceTransaction_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NormalizedAttendanceTransaction_RawAttendanceTransaction_Ra~",
                        column: x => x.RawTransactionId,
                        principalSchema: "Attendance",
                        principalTable: "RawAttendanceTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedAttendanceTransaction_DeviceId",
                schema: "Attendance",
                table: "NormalizedAttendanceTransaction",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedAttendanceTransaction_DuplicateOfTransactionId",
                schema: "Attendance",
                table: "NormalizedAttendanceTransaction",
                column: "DuplicateOfTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedAttendanceTransaction_EmployeeId",
                schema: "Attendance",
                table: "NormalizedAttendanceTransaction",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedAttendanceTransaction_IntegrationId",
                schema: "Attendance",
                table: "NormalizedAttendanceTransaction",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedAttendanceTransaction_RawTransactionId",
                schema: "Attendance",
                table: "NormalizedAttendanceTransaction",
                column: "RawTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedAttendanceTransaction_TenantId_EmployeeId_DeviceI~",
                schema: "Attendance",
                table: "NormalizedAttendanceTransaction",
                columns: new[] { "TenantId", "EmployeeId", "DeviceId", "NormalizedEventType", "DuplicateBucketTimestampUtc" },
                unique: true,
                filter: "\"DuplicateStatus\" = 'Unique'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NormalizedAttendanceTransaction",
                schema: "Attendance");
        }
    }
}
