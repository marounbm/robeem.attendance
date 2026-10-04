using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceCorrectionIds",
                schema: "Attendance",
                table: "AttendanceRecord",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceCorrection",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttendanceExceptionId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CorrectedEventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProposedValue = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedByEmployeeId = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ApprovalWorkflow = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ManagerDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ManagerDecidedById = table.Column<int>(type: "integer", nullable: true),
                    ManagerDecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    HrDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HrDecidedById = table.Column<int>(type: "integer", nullable: true),
                    HrDecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrection_AttendanceException_AttendanceExceptio~",
                        column: x => x.AttendanceExceptionId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceException",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrection_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrection_Profile_HrDecidedById",
                        column: x => x.HrDecidedById,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrection_Profile_ManagerDecidedById",
                        column: x => x.ManagerDecidedById,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrection_Profile_RequestedByEmployeeId",
                        column: x => x.RequestedByEmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_AttendanceExceptionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "AttendanceExceptionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_EmployeeId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_HrDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "HrDecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_ManagerDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "ManagerDecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_RequestedByEmployeeId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "RequestedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_TenantId_AttendanceExceptionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                columns: new[] { "TenantId", "AttendanceExceptionId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Approved')");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_TenantId_EmployeeId_ProposedValue",
                schema: "Attendance",
                table: "AttendanceCorrection",
                columns: new[] { "TenantId", "EmployeeId", "ProposedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_TenantId_Status",
                schema: "Attendance",
                table: "AttendanceCorrection",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCorrection",
                schema: "Attendance");

            migrationBuilder.DropColumn(
                name: "SourceCorrectionIds",
                schema: "Attendance",
                table: "AttendanceRecord");
        }
    }
}
