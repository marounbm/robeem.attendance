using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimestampSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateFormats",
                schema: "Attendance",
                table: "AttendanceFileFormat",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TimeFormats",
                schema: "Attendance",
                table: "AttendanceFileFormat",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TimestampCulture",
                schema: "Attendance",
                table: "AttendanceFileFormat",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateFormats",
                schema: "Attendance",
                table: "AttendanceFileFormat");

            migrationBuilder.DropColumn(
                name: "TimeFormats",
                schema: "Attendance",
                table: "AttendanceFileFormat");

            migrationBuilder.DropColumn(
                name: "TimestampCulture",
                schema: "Attendance",
                table: "AttendanceFileFormat");
        }
    }
}
