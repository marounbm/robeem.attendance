using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceExceptionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExceptionGroupId",
                schema: "Attendance",
                table: "AttendanceException",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceExceptionGroup",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TimeScheduleShiftId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResolutionMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ResolvedById = table.Column<int>(type: "integer", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceExceptionGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceExceptionGroup_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceExceptionGroup_TimeScheduleShift_TimeScheduleShif~",
                        column: x => x.TimeScheduleShiftId,
                        principalSchema: "HR",
                        principalTable: "TimeScheduleShift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceException_ExceptionGroupId",
                schema: "Attendance",
                table: "AttendanceException",
                column: "ExceptionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionGroup_EmployeeId",
                schema: "Attendance",
                table: "AttendanceExceptionGroup",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionGroup_TenantId_EmployeeId_AttendanceDate",
                schema: "Attendance",
                table: "AttendanceExceptionGroup",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionGroup_TenantId_Status",
                schema: "Attendance",
                table: "AttendanceExceptionGroup",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceExceptionGroup_TimeScheduleShiftId",
                schema: "Attendance",
                table: "AttendanceExceptionGroup",
                column: "TimeScheduleShiftId");

            // Group the exceptions that already exist: one group for the exceptions of an employee's shift on a date that are not closed (Open
            // while any is open, else PendingCorrection), and one closed group per shift and date for the closed ones, closed the way the
            // exception resolved last was. The exceptions keep their own status and resolution. Nothing here changes an exception but its group.
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceExceptionGroup"
                    ("TenantId", "EmployeeId", "AttendanceDate", "TimeScheduleShiftId", "Status", "DetectedAt",
                     "ResolvedAt", "ResolutionMethod", "ResolvedById", "ResolutionNote",
                     "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT
                    e."TenantId", e."EmployeeId", e."AttendanceDate", e."TimeScheduleShiftId",
                    CASE WHEN bool_or(e."Status" = 'Open') THEN 'Open' ELSE 'PendingCorrection' END,
                    MIN(e."DetectedAt"),
                    NULL, NULL, NULL, NULL,
                    MIN(e."CreatedOn"), MIN(e."CreatedBy"), MAX(e."ModifiedOn"), MAX(e."ModifiedBy"), gen_random_uuid()
                FROM "Attendance"."AttendanceException" e
                WHERE e."Status" <> 'Closed'
                GROUP BY e."TenantId", e."EmployeeId", e."AttendanceDate", e."TimeScheduleShiftId";
                """);

            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceExceptionGroup"
                    ("TenantId", "EmployeeId", "AttendanceDate", "TimeScheduleShiftId", "Status", "DetectedAt",
                     "ResolvedAt", "ResolutionMethod", "ResolvedById", "ResolutionNote",
                     "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT DISTINCT ON (e."TenantId", e."EmployeeId", e."AttendanceDate", e."TimeScheduleShiftId")
                    e."TenantId", e."EmployeeId", e."AttendanceDate", e."TimeScheduleShiftId", 'Closed',
                    MIN(e."DetectedAt") OVER (PARTITION BY e."TenantId", e."EmployeeId", e."AttendanceDate", e."TimeScheduleShiftId"),
                    e."ResolvedAt", e."ResolutionMethod", e."ResolvedById", e."ResolutionNote",
                    e."CreatedOn", e."CreatedBy", e."ModifiedOn", e."ModifiedBy", gen_random_uuid()
                FROM "Attendance"."AttendanceException" e
                WHERE e."Status" = 'Closed'
                ORDER BY e."TenantId", e."EmployeeId", e."AttendanceDate", e."TimeScheduleShiftId", e."ResolvedAt" DESC NULLS LAST, e."Id" DESC;
                """);

            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceException" e
                SET "ExceptionGroupId" = g."Id"
                FROM "Attendance"."AttendanceExceptionGroup" g
                WHERE e."ExceptionGroupId" IS NULL
                  AND g."TenantId" = e."TenantId"
                  AND g."EmployeeId" = e."EmployeeId"
                  AND g."AttendanceDate" = e."AttendanceDate"
                  AND g."TimeScheduleShiftId" IS NOT DISTINCT FROM e."TimeScheduleShiftId"
                  AND (g."Status" = 'Closed') = (e."Status" = 'Closed');
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceException_AttendanceExceptionGroup_ExceptionGroup~",
                schema: "Attendance",
                table: "AttendanceException",
                column: "ExceptionGroupId",
                principalSchema: "Attendance",
                principalTable: "AttendanceExceptionGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceException_AttendanceExceptionGroup_ExceptionGroup~",
                schema: "Attendance",
                table: "AttendanceException");

            migrationBuilder.DropTable(
                name: "AttendanceExceptionGroup",
                schema: "Attendance");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceException_ExceptionGroupId",
                schema: "Attendance",
                table: "AttendanceException");

            migrationBuilder.DropColumn(
                name: "ExceptionGroupId",
                schema: "Attendance",
                table: "AttendanceException");
        }
    }
}
