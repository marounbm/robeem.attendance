using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreakInference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExcusedMinutes",
                schema: "Attendance",
                table: "AttendanceRecord",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BreakInferenceMode",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Off");

            migrationBuilder.AddColumn<int>(
                name: "InferredBreakMaxMinutes",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<int>(
                name: "InferredBreakWindowMinutes",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 90);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcusedMinutes",
                schema: "Attendance",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "BreakInferenceMode",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "InferredBreakMaxMinutes",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "InferredBreakWindowMinutes",
                schema: "Attendance",
                table: "AttendancePolicy");
        }
    }
}
