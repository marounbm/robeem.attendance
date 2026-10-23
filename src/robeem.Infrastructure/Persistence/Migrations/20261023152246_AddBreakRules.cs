using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreakRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BreakFindings",
                schema: "Attendance",
                table: "AttendanceRecord",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The defaults of a new policy, so that the policies already stored start with the same rules: breaks under 10 minutes are
            // ignored, a break may start 15 minutes off schedule and run 5 over, and a shift worked with no break is still raised as before.
            migrationBuilder.AddColumn<int>(
                name: "BreakStartGraceMinutes",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "IgnoreBreaksShorterThanMinutes",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "LongBreakGraceMinutes",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "RaiseExceptionOnEarlyBreak",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RaiseExceptionOnLateBreak",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RaiseExceptionOnLongBreak",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RaiseExceptionOnNoBreak",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                schema: "Attendance",
                table: "AttendanceException",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedById",
                schema: "Attendance",
                table: "AttendanceException",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakFindings",
                schema: "Attendance",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "BreakStartGraceMinutes",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "IgnoreBreaksShorterThanMinutes",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "LongBreakGraceMinutes",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "RaiseExceptionOnEarlyBreak",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "RaiseExceptionOnLateBreak",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "RaiseExceptionOnLongBreak",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "RaiseExceptionOnNoBreak",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                schema: "Attendance",
                table: "AttendanceException");

            migrationBuilder.DropColumn(
                name: "ResolvedById",
                schema: "Attendance",
                table: "AttendanceException");
        }
    }
}
