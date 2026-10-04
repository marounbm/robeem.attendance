using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MissingPunchGraceMinutes",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.CreateTable(
                name: "AttendanceException",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TimeScheduleShiftId = table.Column<int>(type: "integer", nullable: true),
                    ExceptionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MissingEventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RelatedNormalizedTransactionId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResolutionMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceException", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceException_NormalizedAttendanceTransaction_Related~",
                        column: x => x.RelatedNormalizedTransactionId,
                        principalSchema: "Attendance",
                        principalTable: "NormalizedAttendanceTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceException_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceException_TimeScheduleShift_TimeScheduleShiftId",
                        column: x => x.TimeScheduleShiftId,
                        principalSchema: "HR",
                        principalTable: "TimeScheduleShift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceException_EmployeeId",
                schema: "Attendance",
                table: "AttendanceException",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceException_RelatedNormalizedTransactionId",
                schema: "Attendance",
                table: "AttendanceException",
                column: "RelatedNormalizedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceException_TenantId_EmployeeId_AttendanceDate",
                schema: "Attendance",
                table: "AttendanceException",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceException_TenantId_Status",
                schema: "Attendance",
                table: "AttendanceException",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceException_TimeScheduleShiftId",
                schema: "Attendance",
                table: "AttendanceException",
                column: "TimeScheduleShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceException",
                schema: "Attendance");

            migrationBuilder.DropColumn(
                name: "MissingPunchGraceMinutes",
                schema: "Attendance",
                table: "AttendancePolicy");
        }
    }
}
