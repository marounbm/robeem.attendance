using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceDaySummary",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TotalWorkingHours = table.Column<TimeSpan>(type: "interval", nullable: false),
                    TotalOvertimeHours = table.Column<TimeSpan>(type: "interval", nullable: false),
                    TotalBreakHours = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ShiftCount = table.Column<int>(type: "integer", nullable: false),
                    ExceptionCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDaySummary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceDaySummary_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecord",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TimeScheduleShiftId = table.Column<int>(type: "integer", nullable: true),
                    ScheduledStart = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ScheduledEnd = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActualCheckIn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActualCheckInSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ActualCheckOut = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActualCheckOutSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ActualBreakOut = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActualBreakOutSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ActualBreakIn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ActualBreakInSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WorkingHours = table.Column<TimeSpan>(type: "interval", nullable: false),
                    BreakDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
                    EarlyDepartureMinutes = table.Column<int>(type: "integer", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceNormalizedTransactionIds = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    SupersededByRecordId = table.Column<int>(type: "integer", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecord_AttendanceRecord_SupersededByRecordId",
                        column: x => x.SupersededByRecordId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRecord_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRecord_TimeScheduleShift_TimeScheduleShiftId",
                        column: x => x.TimeScheduleShiftId,
                        principalSchema: "HR",
                        principalTable: "TimeScheduleShift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDaySummary_EmployeeId",
                schema: "Attendance",
                table: "AttendanceDaySummary",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDaySummary_TenantId_EmployeeId_AttendanceDate",
                schema: "Attendance",
                table: "AttendanceDaySummary",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_EmployeeId",
                schema: "Attendance",
                table: "AttendanceRecord",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_SupersededByRecordId",
                schema: "Attendance",
                table: "AttendanceRecord",
                column: "SupersededByRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_TenantId_EmployeeId_AttendanceDate_IsCurre~",
                schema: "Attendance",
                table: "AttendanceRecord",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_TenantId_EmployeeId_AttendanceDate_TimeSch~",
                schema: "Attendance",
                table: "AttendanceRecord",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate", "TimeScheduleShiftId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_TimeScheduleShiftId",
                schema: "Attendance",
                table: "AttendanceRecord",
                column: "TimeScheduleShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceDaySummary",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceRecord",
                schema: "Attendance");
        }
    }
}
