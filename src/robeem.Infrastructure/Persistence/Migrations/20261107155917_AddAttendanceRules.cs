using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RuleSetVersion",
                schema: "Attendance",
                table: "AttendanceRecord",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AttendanceRuleSet",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TimeScheduleId = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PublishedById = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRuleSet", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRuleSet_TimeSchedule_TimeScheduleId",
                        column: x => x.TimeScheduleId,
                        principalSchema: "HR",
                        principalTable: "TimeSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRule",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttendanceRuleSetId = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Finding = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRule_AttendanceRuleSet_AttendanceRuleSetId",
                        column: x => x.AttendanceRuleSetId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceRuleSet",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRuleCondition",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttendanceRuleId = table.Column<int>(type: "integer", nullable: false),
                    Fact = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Operator = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRuleCondition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRuleCondition_AttendanceRule_AttendanceRuleId",
                        column: x => x.AttendanceRuleId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRule_AttendanceRuleSetId",
                schema: "Attendance",
                table: "AttendanceRule",
                column: "AttendanceRuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRule_TenantId_AttendanceRuleSetId_Priority",
                schema: "Attendance",
                table: "AttendanceRule",
                columns: new[] { "TenantId", "AttendanceRuleSetId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRuleCondition_AttendanceRuleId",
                schema: "Attendance",
                table: "AttendanceRuleCondition",
                column: "AttendanceRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRuleCondition_TenantId_AttendanceRuleId",
                schema: "Attendance",
                table: "AttendanceRuleCondition",
                columns: new[] { "TenantId", "AttendanceRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRuleSet_TenantId_TimeScheduleId_Status",
                schema: "Attendance",
                table: "AttendanceRuleSet",
                columns: new[] { "TenantId", "TimeScheduleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRuleSet_TenantId_Version",
                schema: "Attendance",
                table: "AttendanceRuleSet",
                columns: new[] { "TenantId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRuleSet_TimeScheduleId",
                schema: "Attendance",
                table: "AttendanceRuleSet",
                column: "TimeScheduleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRuleCondition",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceRule",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceRuleSet",
                schema: "Attendance");

            migrationBuilder.DropColumn(
                name: "RuleSetVersion",
                schema: "Attendance",
                table: "AttendanceRecord");
        }
    }
}
