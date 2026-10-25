using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayloadFormatVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendancePayloadFormat_TenantId_Name",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.AddColumn<int>(
                name: "SupersedesId",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Every upload batch that predates the record says which format read it: the one its integration used (its own, else its
            // connector's default), which is the one in force when the batch was read as far as anything can now tell. From here on a
            // change to that format is a new version, so these batches keep being read the way they were.
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceProcessingBatch" b
                SET "PayloadFormatId" = COALESCE(i."PayloadFormatId", c."DefaultPayloadFormatId")
                FROM "Attendance"."AttendanceIntegration" i
                JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."Id" = i."ConnectorId"
                WHERE b."IntegrationId" = i."Id" AND b."BatchType" = 'ManualUpload' AND b."PayloadFormatId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadFormat_SupersedesId",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                column: "SupersedesId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadFormat_TenantId_Name_Version",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                columns: new[] { "TenantId", "Name", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePayloadFormat_AttendancePayloadFormat_SupersedesId",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                column: "SupersedesId",
                principalSchema: "Attendance",
                principalTable: "AttendancePayloadFormat",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePayloadFormat_AttendancePayloadFormat_SupersedesId",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePayloadFormat_SupersedesId",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.DropIndex(
                name: "IX_AttendancePayloadFormat_TenantId_Name_Version",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.DropColumn(
                name: "SupersedesId",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadFormat_TenantId_Name",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }
    }
}
