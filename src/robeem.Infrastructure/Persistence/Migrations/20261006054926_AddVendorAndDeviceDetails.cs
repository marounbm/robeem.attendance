using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorAndDeviceDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                schema: "Attendance",
                table: "AttendanceProvider",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                schema: "Attendance",
                table: "AttendanceProvider",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SdkVersion",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SoftwareVersion",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Manufacturer",
                schema: "Attendance",
                table: "AttendanceProvider");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                schema: "Attendance",
                table: "AttendanceProvider");

            migrationBuilder.DropColumn(
                name: "Model",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropColumn(
                name: "SdkVersion",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropColumn(
                name: "SoftwareVersion",
                schema: "Attendance",
                table: "AttendanceDevice");
        }
    }
}
