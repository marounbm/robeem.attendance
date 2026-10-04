using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "HR");

            migrationBuilder.CreateTable(
                name: "Profile",
                schema: "HR",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmployeeNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JobTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HireDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TerminationDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeSchedule",
                schema: "HR",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CyclePeriod = table.Column<int>(type: "integer", nullable: false),
                    FirstCycleDay = table.Column<int>(type: "integer", nullable: false),
                    Scheduling = table.Column<bool>(type: "boolean", nullable: false),
                    LastScheduleDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastScheduleDay = table.Column<int>(type: "integer", nullable: false),
                    SystemSchedule = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeSchedule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeScheduleShift",
                schema: "HR",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StartTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    EndTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Overtime = table.Column<bool>(type: "boolean", nullable: false),
                    BreakTime = table.Column<bool>(type: "boolean", nullable: false),
                    BreakStartTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    BreakEndTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    TimeScheduleId = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeScheduleShift", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeScheduleShift_TimeSchedule_TimeScheduleId",
                        column: x => x.TimeScheduleId,
                        principalSchema: "HR",
                        principalTable: "TimeSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimeScheduleTeam",
                schema: "HR",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TimeScheduleId = table.Column<int>(type: "integer", nullable: false),
                    AllInclusive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeScheduleTeam", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeScheduleTeam_TimeSchedule_TimeScheduleId",
                        column: x => x.TimeScheduleId,
                        principalSchema: "HR",
                        principalTable: "TimeSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimeScheduleEmployee",
                schema: "HR",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimeScheduleId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeScheduleEmployee", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeScheduleEmployee_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeScheduleEmployee_TimeScheduleTeam_TeamId",
                        column: x => x.TeamId,
                        principalSchema: "HR",
                        principalTable: "TimeScheduleTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeScheduleEmployee_TimeSchedule_TimeScheduleId",
                        column: x => x.TimeScheduleId,
                        principalSchema: "HR",
                        principalTable: "TimeSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimeScheduleLine",
                schema: "HR",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimeScheduleId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ShiftId = table.Column<int>(type: "integer", nullable: false),
                    CycleDay = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeScheduleLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeScheduleLine_TimeScheduleShift_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "HR",
                        principalTable: "TimeScheduleShift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeScheduleLine_TimeScheduleTeam_TeamId",
                        column: x => x.TeamId,
                        principalSchema: "HR",
                        principalTable: "TimeScheduleTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeScheduleLine_TimeSchedule_TimeScheduleId",
                        column: x => x.TimeScheduleId,
                        principalSchema: "HR",
                        principalTable: "TimeSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Profile_LastName_FirstName",
                schema: "HR",
                table: "Profile",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Profile_TenantId_EmployeeNumber",
                schema: "HR",
                table: "Profile",
                columns: new[] { "TenantId", "EmployeeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleEmployee_EmployeeId",
                schema: "HR",
                table: "TimeScheduleEmployee",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleEmployee_TeamId",
                schema: "HR",
                table: "TimeScheduleEmployee",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleEmployee_TenantId_EmployeeId",
                schema: "HR",
                table: "TimeScheduleEmployee",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleEmployee_TimeScheduleId",
                schema: "HR",
                table: "TimeScheduleEmployee",
                column: "TimeScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleLine_ShiftId",
                schema: "HR",
                table: "TimeScheduleLine",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleLine_TeamId",
                schema: "HR",
                table: "TimeScheduleLine",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleLine_TenantId_TimeScheduleId_TeamId_CycleDay_Sh~",
                schema: "HR",
                table: "TimeScheduleLine",
                columns: new[] { "TenantId", "TimeScheduleId", "TeamId", "CycleDay", "ShiftId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleLine_TimeScheduleId",
                schema: "HR",
                table: "TimeScheduleLine",
                column: "TimeScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleShift_TimeScheduleId",
                schema: "HR",
                table: "TimeScheduleShift",
                column: "TimeScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeScheduleTeam_TimeScheduleId",
                schema: "HR",
                table: "TimeScheduleTeam",
                column: "TimeScheduleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeScheduleEmployee",
                schema: "HR");

            migrationBuilder.DropTable(
                name: "TimeScheduleLine",
                schema: "HR");

            migrationBuilder.DropTable(
                name: "Profile",
                schema: "HR");

            migrationBuilder.DropTable(
                name: "TimeScheduleShift",
                schema: "HR");

            migrationBuilder.DropTable(
                name: "TimeScheduleTeam",
                schema: "HR");

            migrationBuilder.DropTable(
                name: "TimeSchedule",
                schema: "HR");
        }
    }
}
