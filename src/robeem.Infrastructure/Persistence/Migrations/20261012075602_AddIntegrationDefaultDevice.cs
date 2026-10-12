using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationDefaultDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultDeviceId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceIntegration_DefaultDeviceId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "DefaultDeviceId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceIntegration_AttendanceDevice_DefaultDeviceId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "DefaultDeviceId",
                principalSchema: "Attendance",
                principalTable: "AttendanceDevice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceIntegration_AttendanceDevice_DefaultDeviceId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceIntegration_DefaultDeviceId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "DefaultDeviceId",
                schema: "Attendance",
                table: "AttendanceIntegration");
        }
    }
}
