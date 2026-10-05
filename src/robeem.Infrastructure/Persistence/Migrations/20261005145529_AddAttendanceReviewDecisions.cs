using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceReviewDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceReviewDecision",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttendanceRecordId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedById = table.Column<int>(type: "integer", nullable: false),
                    DecidedByRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OvertimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceReviewDecision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceReviewDecision_AttendanceRecord_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceReviewDecision_Profile_DecidedById",
                        column: x => x.DecidedById,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceReviewDecision_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewDecision_AttendanceRecordId",
                schema: "Attendance",
                table: "AttendanceReviewDecision",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewDecision_DecidedById",
                schema: "Attendance",
                table: "AttendanceReviewDecision",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewDecision_EmployeeId",
                schema: "Attendance",
                table: "AttendanceReviewDecision",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewDecision_TenantId_AttendanceDate_Outcome",
                schema: "Attendance",
                table: "AttendanceReviewDecision",
                columns: new[] { "TenantId", "AttendanceDate", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceReviewDecision_TenantId_AttendanceRecordId",
                schema: "Attendance",
                table: "AttendanceReviewDecision",
                columns: new[] { "TenantId", "AttendanceRecordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceReviewDecision",
                schema: "Attendance");
        }
    }
}
