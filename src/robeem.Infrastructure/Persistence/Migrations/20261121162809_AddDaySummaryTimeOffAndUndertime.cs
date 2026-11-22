using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDaySummaryTimeOffAndUndertime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "TotalTimeOffHours",
                schema: "Attendance",
                table: "AttendanceDaySummary",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TotalUndertimeHours",
                schema: "Attendance",
                table: "AttendanceDaySummary",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalTimeOffHours",
                schema: "Attendance",
                table: "AttendanceDaySummary");

            migrationBuilder.DropColumn(
                name: "TotalUndertimeHours",
                schema: "Attendance",
                table: "AttendanceDaySummary");
        }
    }
}
