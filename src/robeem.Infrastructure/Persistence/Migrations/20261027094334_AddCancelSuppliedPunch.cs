using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelSuppliedPunch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CancelsCorrectionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_CancelsCorrectionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "CancelsCorrectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrection_AttendanceCorrection_CancelsCorrection~",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "CancelsCorrectionId",
                principalSchema: "Attendance",
                principalTable: "AttendanceCorrection",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrection_AttendanceCorrection_CancelsCorrection~",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_CancelsCorrectionId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "CancelsCorrectionId",
                schema: "Attendance",
                table: "AttendanceCorrection");
        }
    }
}
