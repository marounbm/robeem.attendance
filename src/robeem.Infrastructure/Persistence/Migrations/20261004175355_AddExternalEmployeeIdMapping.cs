using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalEmployeeIdMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalEmployeeIdMapping",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<int>(type: "integer", nullable: false),
                    ExternalEmployeeId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalEmployeeIdMapping", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalEmployeeIdMapping_AttendanceIntegration_Integration~",
                        column: x => x.IntegrationId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceIntegration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalEmployeeIdMapping_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeIdMapping_EmployeeId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeIdMapping_IntegrationId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeIdMapping_TenantId_IntegrationId_ExternalEm~",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                columns: new[] { "TenantId", "IntegrationId", "ExternalEmployeeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalEmployeeIdMapping",
                schema: "Attendance");
        }
    }
}
