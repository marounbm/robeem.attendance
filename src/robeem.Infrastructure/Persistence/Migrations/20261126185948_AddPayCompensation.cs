using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayCompensation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayPeriod_TenantId_StartDate_EndDate",
                schema: "Attendance",
                table: "PayPeriod");

            migrationBuilder.AddColumn<int>(
                name: "EmployeeId",
                schema: "Attendance",
                table: "PayPeriod",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PayDayOffWorkIsOvertime",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PayEarlyPenaltyPercent",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PayHolidayWorkIsOvertime",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PayLatePenaltyPercent",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PayUndertimePenaltyPercent",
                schema: "Attendance",
                table: "AttendancePolicy",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.CreateTable(
                name: "PayCompensationDecision",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LineKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    LeaveIds = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DecidedById = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayCompensationDecision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayCompensationDecision_Profile_DecidedById",
                        column: x => x.DecidedById,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayCompensationDecision_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayCycle",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: true),
                    Frequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AnchorDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StartDay = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayCycle", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayCycle_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayPeriod_EmployeeId",
                schema: "Attendance",
                table: "PayPeriod",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayPeriod_TenantId_EmployeeId_StartDate_EndDate",
                schema: "Attendance",
                table: "PayPeriod",
                columns: new[] { "TenantId", "EmployeeId", "StartDate", "EndDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayPeriod_TenantId_StartDate_EndDate_CompanyWide",
                schema: "Attendance",
                table: "PayPeriod",
                columns: new[] { "TenantId", "StartDate", "EndDate" },
                unique: true,
                filter: "\"EmployeeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayCompensationDecision_DecidedById",
                schema: "Attendance",
                table: "PayCompensationDecision",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_PayCompensationDecision_EmployeeId",
                schema: "Attendance",
                table: "PayCompensationDecision",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayCompensationDecision_TenantId_EmployeeId_AttendanceDate",
                schema: "Attendance",
                table: "PayCompensationDecision",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PayCycle_EmployeeId",
                schema: "Attendance",
                table: "PayCycle",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayCycle_TenantId",
                schema: "Attendance",
                table: "PayCycle",
                column: "TenantId",
                unique: true,
                filter: "\"EmployeeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayCycle_TenantId_EmployeeId",
                schema: "Attendance",
                table: "PayCycle",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "\"EmployeeId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PayPeriod_Profile_EmployeeId",
                schema: "Attendance",
                table: "PayPeriod",
                column: "EmployeeId",
                principalSchema: "HR",
                principalTable: "Profile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayPeriod_Profile_EmployeeId",
                schema: "Attendance",
                table: "PayPeriod");

            migrationBuilder.DropTable(
                name: "PayCompensationDecision",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "PayCycle",
                schema: "Attendance");

            migrationBuilder.DropIndex(
                name: "IX_PayPeriod_EmployeeId",
                schema: "Attendance",
                table: "PayPeriod");

            migrationBuilder.DropIndex(
                name: "IX_PayPeriod_TenantId_EmployeeId_StartDate_EndDate",
                schema: "Attendance",
                table: "PayPeriod");

            migrationBuilder.DropIndex(
                name: "IX_PayPeriod_TenantId_StartDate_EndDate_CompanyWide",
                schema: "Attendance",
                table: "PayPeriod");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                schema: "Attendance",
                table: "PayPeriod");

            migrationBuilder.DropColumn(
                name: "PayDayOffWorkIsOvertime",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "PayEarlyPenaltyPercent",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "PayHolidayWorkIsOvertime",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "PayLatePenaltyPercent",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.DropColumn(
                name: "PayUndertimePenaltyPercent",
                schema: "Attendance",
                table: "AttendancePolicy");

            migrationBuilder.CreateIndex(
                name: "IX_PayPeriod_TenantId_StartDate_EndDate",
                schema: "Attendance",
                table: "PayPeriod",
                columns: new[] { "TenantId", "StartDate", "EndDate" },
                unique: true);
        }
    }
}
