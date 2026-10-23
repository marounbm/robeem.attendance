using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPunchTypeCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RelatedEventAt",
                schema: "Attendance",
                table: "AttendanceException",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEventType",
                schema: "Attendance",
                table: "AttendanceException",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedEventTypes",
                schema: "Attendance",
                table: "AttendanceException",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "SupplyPunch");

            migrationBuilder.AddColumn<int>(
                name: "NormalizedTransactionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalEventType",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_NormalizedTransactionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "NormalizedTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrection_NormalizedAttendanceTransaction_Normal~",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "NormalizedTransactionId",
                principalSchema: "Attendance",
                principalTable: "NormalizedAttendanceTransaction",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrection_NormalizedAttendanceTransaction_Normal~",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_NormalizedTransactionId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "RelatedEventAt",
                schema: "Attendance",
                table: "AttendanceException");

            migrationBuilder.DropColumn(
                name: "RelatedEventType",
                schema: "Attendance",
                table: "AttendanceException");

            migrationBuilder.DropColumn(
                name: "SuggestedEventTypes",
                schema: "Attendance",
                table: "AttendanceException");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "NormalizedTransactionId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "OriginalEventType",
                schema: "Attendance",
                table: "AttendanceCorrection");
        }
    }
}
