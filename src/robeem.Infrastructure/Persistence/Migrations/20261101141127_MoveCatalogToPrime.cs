using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-written (the scaffold would drop the columns before reading them). The attendance catalog moves to the prime tenant's
    /// database (CatalogDbContext), so a tenant keeps no copy of it: what pointed at a catalog row by id (an integration's
    /// connector and payload format, a device's type, a batch's payload format) now names it by key, and the key is read from the
    /// catalog row before that row is dropped.
    ///
    /// Destructive on purpose: whatever a tenant had added to its own catalog (a custom vendor, connector or payload format) and any
    /// transport it had renamed is dropped with the tables. Only the references survive, as keys; the built-in catalog is re-seeded
    /// in the prime tenant's database, where a platform administrator maintains it.
    /// </remarks>
    public partial class MoveCatalogToPrime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The new key columns, empty for now.
            migrationBuilder.AddColumn<string>(
                name: "ConnectorKey",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadFormatName",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceTypeName",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadFormatName",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayloadFormatVersion",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                type: "integer",
                nullable: true);

            // 2. Read each key from the catalog row the old id pointed at.
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceIntegration" i
                SET "ConnectorKey" = c."ConnectorKey"
                FROM "Attendance"."AttendanceConnectorDefinition" c
                WHERE c."Id" = i."ConnectorId" AND c."TenantId" = i."TenantId";
                """);

            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceIntegration" i
                SET "PayloadFormatName" = f."Name"
                FROM "Attendance"."AttendancePayloadFormat" f
                WHERE f."Id" = i."PayloadFormatId" AND f."TenantId" = i."TenantId";
                """);

            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceDevice" d
                SET "DeviceTypeName" = t."Name"
                FROM "Attendance"."AttendanceDeviceType" t
                WHERE t."Id" = d."DeviceTypeId" AND t."TenantId" = d."TenantId";
                """);

            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceProcessingBatch" b
                SET "PayloadFormatName" = f."Name", "PayloadFormatVersion" = f."Version"
                FROM "Attendance"."AttendancePayloadFormat" f
                WHERE f."Id" = b."PayloadFormatId" AND f."TenantId" = b."TenantId";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ConnectorKey",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // 3. Nothing points at the catalog tables any more: drop the foreign keys, the tables, then the old id columns.
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceDevice_AttendanceDeviceType_DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceIntegration_AttendanceConnectorDefinition_Connect~",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceIntegration_AttendancePayloadFormat_PayloadFormat~",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceProcessingBatch_AttendancePayloadFormat_PayloadFo~",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceConnectorDefinition_AttendancePayloadFormat_Defau~",
                schema: "Attendance",
                table: "AttendanceConnectorDefinition");

            migrationBuilder.DropTable(
                name: "AttendanceConnectorTransport",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceDeviceType",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendancePayloadEventCode",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceTransportDefinition",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendancePayloadFormat",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceConnectorDefinition",
                schema: "Attendance");

            migrationBuilder.DropTable(
                name: "AttendanceVendor",
                schema: "Attendance");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceProcessingBatch_PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceIntegration_ConnectorId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceIntegration_PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceDevice_DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropColumn(
                name: "ConnectorId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "DeviceTypeId",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropColumn(
                name: "PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            // 4. The indexes of the new columns.
            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProcessingBatch_PayloadFormatName_PayloadFormatVe~",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                columns: new[] { "PayloadFormatName", "PayloadFormatVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceIntegration_ConnectorKey",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "ConnectorKey");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceIntegration_PayloadFormatName",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "PayloadFormatName");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevice_DeviceTypeName",
                schema: "Attendance",
                table: "AttendanceDevice",
                column: "DeviceTypeName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "MoveCatalogToPrime can't be reversed: the tenant's catalog tables were dropped, and the catalog now lives in the prime tenant's database.");
        }
    }
}
