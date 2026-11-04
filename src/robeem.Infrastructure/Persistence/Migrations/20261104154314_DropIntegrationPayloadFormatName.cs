using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropIntegrationPayloadFormatName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An integration no longer has a payload format of its own: its files are read by its connector's default, in the
            // prime tenant's catalog. Whatever an integration had chosen is dropped here, and Down cannot bring it back. Batches that
            // were already read keep the name and version they recorded (AttendanceProcessingBatch), so they are read as before.
            migrationBuilder.DropIndex(
                name: "IX_AttendanceIntegration_PayloadFormatName",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "PayloadFormatName",
                schema: "Attendance",
                table: "AttendanceIntegration");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadFormatName",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceIntegration_PayloadFormatName",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "PayloadFormatName");
        }
    }
}
