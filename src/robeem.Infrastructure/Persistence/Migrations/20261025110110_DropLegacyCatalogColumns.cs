using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyCatalogColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The seeder used to move these two columns into the catalog tables the next time the Api started in Development. A database
            // that never ran it since the catalog became data still holds that text, so it is moved here, the same way, before the
            // columns go: a vendor's transports onto its main connector (the one with its key, else the oldest), a device's type onto
            // a device type row (created when the name is not listed). Rows the seeder already moved have empty text and are untouched.
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceConnectorTransport" ("TenantId", "ConnectorId", "TransportId")
                SELECT DISTINCT v."TenantId", c."Id", td."Id"
                FROM "Attendance"."AttendanceVendor" v
                CROSS JOIN LATERAL regexp_split_to_table(v."SupportedTransports", '\s*,\s*') AS k(key)
                JOIN LATERAL (
                    SELECT "Id" FROM "Attendance"."AttendanceConnectorDefinition"
                    WHERE "VendorId" = v."Id"
                    ORDER BY ("ConnectorKey" = v."VendorKey") DESC, "Id"
                    LIMIT 1) c ON TRUE
                JOIN "Attendance"."AttendanceTransportDefinition" td
                    ON td."TenantId" = v."TenantId" AND lower(td."TransportKey") = lower(trim(k.key)) AND td."TransportKey" <> 'FileUpload'
                WHERE v."SupportedTransports" <> ''
                  AND NOT EXISTS (SELECT 1 FROM "Attendance"."AttendanceConnectorTransport" ct WHERE ct."ConnectorId" = c."Id" AND ct."TransportId" = td."Id");

                INSERT INTO "Attendance"."AttendanceDeviceType" ("TenantId", "Name", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT DISTINCT ON (d."TenantId", lower(d."DeviceType")) d."TenantId", d."DeviceType", TRUE, LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()
                FROM "Attendance"."AttendanceDevice" d
                WHERE d."DeviceTypeId" IS NULL AND d."DeviceType" <> ''
                  AND NOT EXISTS (SELECT 1 FROM "Attendance"."AttendanceDeviceType" t WHERE t."TenantId" = d."TenantId" AND lower(t."Name") = lower(d."DeviceType"))
                ORDER BY d."TenantId", lower(d."DeviceType"), d."Id";

                UPDATE "Attendance"."AttendanceDevice" d
                SET "DeviceTypeId" = t."Id"
                FROM "Attendance"."AttendanceDeviceType" t
                WHERE d."DeviceTypeId" IS NULL AND d."DeviceType" <> '' AND t."TenantId" = d."TenantId" AND lower(t."Name") = lower(d."DeviceType");
                """);

            migrationBuilder.DropColumn(
                name: "SupportedTransports",
                schema: "Attendance",
                table: "AttendanceVendor");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                schema: "Attendance",
                table: "AttendanceDevice");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupportedTransports",
                schema: "Attendance",
                table: "AttendanceVendor",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // The device types go back as text; the vendors' transports stay in the connector tables, where they now belong.
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceDevice" d
                SET "DeviceType" = left(t."Name", 32)
                FROM "Attendance"."AttendanceDeviceType" t
                WHERE d."DeviceTypeId" = t."Id";
                """);
        }
    }
}
