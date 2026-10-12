using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceUploadedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceUploadedFile",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<int>(type: "integer", nullable: false),
                    BatchId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedById = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceUploadedFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceUploadedFile_AttendanceIntegration_IntegrationId",
                        column: x => x.IntegrationId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceIntegration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceUploadedFile_AttendanceProcessingBatch_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceProcessingBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceUploadedFile_Profile_UploadedById",
                        column: x => x.UploadedById,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceUploadedFile_BatchId",
                schema: "Attendance",
                table: "AttendanceUploadedFile",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceUploadedFile_IntegrationId",
                schema: "Attendance",
                table: "AttendanceUploadedFile",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceUploadedFile_TenantId_IntegrationId_Id",
                schema: "Attendance",
                table: "AttendanceUploadedFile",
                columns: new[] { "TenantId", "IntegrationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceUploadedFile_TenantId_Sha256",
                schema: "Attendance",
                table: "AttendanceUploadedFile",
                columns: new[] { "TenantId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceUploadedFile_UploadedById",
                schema: "Attendance",
                table: "AttendanceUploadedFile",
                column: "UploadedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceUploadedFile",
                schema: "Attendance");
        }
    }
}
