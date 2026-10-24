using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDayCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "AttendanceExceptionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Corrections proposed so far were each a request of their own. A shared empty id would make them one request, decided together.
            migrationBuilder.Sql("""UPDATE "Attendance"."AttendanceCorrection" SET "RequestId" = gen_random_uuid();""");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_TenantId_RequestId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                columns: new[] { "TenantId", "RequestId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_TenantId_RequestId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "RequestId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            // A correction of a whole day names no exception, and the old model requires one: those rows cannot be kept.
            migrationBuilder.Sql("""DELETE FROM "Attendance"."AttendanceCorrection" WHERE "AttendanceExceptionId" IS NULL;""");

            migrationBuilder.AlterColumn<int>(
                name: "AttendanceExceptionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
