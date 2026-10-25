using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredPayloadsAndDecryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordPath",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PayloadKeyRef",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordPath",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.DropColumn(
                name: "PayloadKeyRef",
                schema: "Attendance",
                table: "AttendanceIntegration");
        }
    }
}
