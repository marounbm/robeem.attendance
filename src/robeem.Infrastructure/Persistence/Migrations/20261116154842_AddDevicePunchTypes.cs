using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDevicePunchTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PunchTypes",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "InOut");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PunchTypes",
                schema: "Attendance",
                table: "AttendanceDevice");
        }
    }
}
