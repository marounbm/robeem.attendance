using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnDemandTimeSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnDemandTimeSchedule",
                schema: "HR",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TimeScheduleId = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnDemandTimeSchedule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnDemandTimeSchedule_TimeSchedule_TimeScheduleId",
                        column: x => x.TimeScheduleId,
                        principalSchema: "HR",
                        principalTable: "TimeSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnDemandTimeSchedule_TenantId_TimeScheduleId",
                schema: "HR",
                table: "OnDemandTimeSchedule",
                columns: new[] { "TenantId", "TimeScheduleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnDemandTimeSchedule_TimeScheduleId",
                schema: "HR",
                table: "OnDemandTimeSchedule",
                column: "TimeScheduleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnDemandTimeSchedule",
                schema: "HR");
        }
    }
}
