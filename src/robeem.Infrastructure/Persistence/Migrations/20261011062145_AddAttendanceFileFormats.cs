using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceFileFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceFileFormat",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FileKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Delimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    HasHeaderRow = table.Column<bool>(type: "boolean", nullable: false),
                    SheetNumber = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmployeeColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TimestampColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DateColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TimeColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TypeColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DirectionColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeviceColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TransactionIdColumn = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TimestampFormats = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceFileFormat", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceFileFormatEventCode",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileFormatId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceFileFormatEventCode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceFileFormatEventCode_AttendanceFileFormat_FileForm~",
                        column: x => x.FileFormatId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceFileFormat",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceIntegration_FileFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "FileFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceFileFormat_TenantId_Name",
                schema: "Attendance",
                table: "AttendanceFileFormat",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceFileFormatEventCode_FileFormatId",
                schema: "Attendance",
                table: "AttendanceFileFormatEventCode",
                column: "FileFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceFileFormatEventCode_TenantId_FileFormatId_Code",
                schema: "Attendance",
                table: "AttendanceFileFormatEventCode",
                columns: new[] { "TenantId", "FileFormatId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceIntegration_AttendanceFileFormat_FileFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "FileFormatId",
                principalSchema: "Attendance",
                principalTable: "AttendanceFileFormat",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceIntegration_AttendanceFileFormat_FileFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropTable(
                name: "AttendanceFileFormatEventCode",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceFileFormat",
                schema: "Attendance");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceIntegration_FileFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "FileFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration");
        }
    }
}
