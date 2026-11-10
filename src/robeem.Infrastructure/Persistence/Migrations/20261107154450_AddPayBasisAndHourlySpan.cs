using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayBasisAndHourlySpan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayBasis",
                schema: "HR",
                table: "Profile",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Scheduled");

            migrationBuilder.AddColumn<int>(
                name: "HourlyMaxSpanHours",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 16);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayBasis",
                schema: "HR",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "HourlyMaxSpanHours",
                schema: "Attendance",
                table: "AttendancePolicy");
        }
    }
}
