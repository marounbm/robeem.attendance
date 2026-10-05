using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceAuditLog",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAuditLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAuditLog_TenantId_Action_Id",
                schema: "Attendance",
                table: "AttendanceAuditLog",
                columns: new[] { "TenantId", "Action", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAuditLog_TenantId_TargetType_TargetId",
                schema: "Attendance",
                table: "AttendanceAuditLog",
                columns: new[] { "TenantId", "TargetType", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceAuditLog",
                schema: "Attendance");
        }
    }
}
