using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Provider becomes Vendor and FileFormat becomes PayloadFormat. Written by hand: the scaffold of a renamed entity is
    /// a drop and a create, which would lose every row. Tables, columns, indexes, primary keys and foreign keys are
    /// renamed in place, so the data and the names EF expects both survive. VendorKey values are untouched: raw punches
    /// store them as their source system.
    /// </summary>
    public partial class RenameVendorAndPayloadFormat : Migration
    {
        private const string SchemaName = "Attendance";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "AttendanceFileFormat", schema: SchemaName, newName: "AttendancePayloadFormat", newSchema: SchemaName);
            migrationBuilder.RenameTable(name: "AttendanceFileFormatEventCode", schema: SchemaName, newName: "AttendancePayloadEventCode", newSchema: SchemaName);
            migrationBuilder.RenameTable(name: "AttendanceProvider", schema: SchemaName, newName: "AttendanceVendor", newSchema: SchemaName);
            migrationBuilder.RenameTable(name: "AttendanceProviderTransport", schema: SchemaName, newName: "AttendanceVendorTransport", newSchema: SchemaName);

            migrationBuilder.RenameColumn(name: "FileKind", schema: SchemaName, table: "AttendancePayloadFormat", newName: "PayloadKind");
            migrationBuilder.RenameColumn(name: "FileFormatId", schema: SchemaName, table: "AttendancePayloadEventCode", newName: "PayloadFormatId");
            migrationBuilder.RenameColumn(name: "ProviderKey", schema: SchemaName, table: "AttendanceVendor", newName: "VendorKey");
            migrationBuilder.RenameColumn(name: "DefaultFileFormatId", schema: SchemaName, table: "AttendanceVendor", newName: "DefaultPayloadFormatId");
            migrationBuilder.RenameColumn(name: "ProviderId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "VendorId");
            migrationBuilder.RenameColumn(name: "UsesFileFormat", schema: SchemaName, table: "AttendanceTransportDefinition", newName: "UsesPayloadFormat");
            migrationBuilder.RenameColumn(name: "ProviderId", schema: SchemaName, table: "AttendanceIntegration", newName: "VendorId");
            migrationBuilder.RenameColumn(name: "FileFormatId", schema: SchemaName, table: "AttendanceIntegration", newName: "PayloadFormatId");

            migrationBuilder.RenameIndex(name: "IX_AttendanceFileFormat_TenantId_Name", schema: SchemaName, table: "AttendancePayloadFormat", newName: "IX_AttendancePayloadFormat_TenantId_Name");
            migrationBuilder.RenameIndex(name: "IX_AttendanceFileFormatEventCode_FileFormatId", schema: SchemaName, table: "AttendancePayloadEventCode", newName: "IX_AttendancePayloadEventCode_PayloadFormatId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceFileFormatEventCode_TenantId_FileFormatId_Code", schema: SchemaName, table: "AttendancePayloadEventCode", newName: "IX_AttendancePayloadEventCode_TenantId_PayloadFormatId_Code");
            migrationBuilder.RenameIndex(name: "IX_AttendanceProvider_DefaultFileFormatId", schema: SchemaName, table: "AttendanceVendor", newName: "IX_AttendanceVendor_DefaultPayloadFormatId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceProvider_TenantId_ProviderKey", schema: SchemaName, table: "AttendanceVendor", newName: "IX_AttendanceVendor_TenantId_VendorKey");
            migrationBuilder.RenameIndex(name: "IX_AttendanceProviderTransport_ProviderId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "IX_AttendanceVendorTransport_VendorId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceProviderTransport_TenantId_ProviderId_TransportId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "IX_AttendanceVendorTransport_TenantId_VendorId_TransportId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceProviderTransport_TransportId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "IX_AttendanceVendorTransport_TransportId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceIntegration_ProviderId", schema: SchemaName, table: "AttendanceIntegration", newName: "IX_AttendanceIntegration_VendorId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceIntegration_FileFormatId", schema: SchemaName, table: "AttendanceIntegration", newName: "IX_AttendanceIntegration_PayloadFormatId");

            RenameConstraint(migrationBuilder, "AttendancePayloadFormat", "PK_AttendanceFileFormat", "PK_AttendancePayloadFormat");
            RenameConstraint(migrationBuilder, "AttendancePayloadEventCode", "PK_AttendanceFileFormatEventCode", "PK_AttendancePayloadEventCode");
            RenameConstraint(migrationBuilder, "AttendanceVendor", "PK_AttendanceProvider", "PK_AttendanceVendor");
            RenameConstraint(migrationBuilder, "AttendanceVendorTransport", "PK_AttendanceProviderTransport", "PK_AttendanceVendorTransport");

            RenameConstraint(migrationBuilder, "AttendancePayloadEventCode", "FK_AttendanceFileFormatEventCode_AttendanceFileFormat_FileForm~", "FK_AttendancePayloadEventCode_AttendancePayloadFormat_PayloadF~");
            RenameConstraint(migrationBuilder, "AttendanceVendor", "FK_AttendanceProvider_AttendanceFileFormat_DefaultFileFormatId", "FK_AttendanceVendor_AttendancePayloadFormat_DefaultPayloadForm~");
            RenameConstraint(migrationBuilder, "AttendanceVendorTransport", "FK_AttendanceProviderTransport_AttendanceProvider_ProviderId", "FK_AttendanceVendorTransport_AttendanceVendor_VendorId");
            RenameConstraint(migrationBuilder, "AttendanceVendorTransport", "FK_AttendanceProviderTransport_AttendanceTransportDefinition_T~", "FK_AttendanceVendorTransport_AttendanceTransportDefinition_Tra~");
            RenameConstraint(migrationBuilder, "AttendanceIntegration", "FK_AttendanceIntegration_AttendanceFileFormat_FileFormatId", "FK_AttendanceIntegration_AttendancePayloadFormat_PayloadFormat~");
            RenameConstraint(migrationBuilder, "AttendanceIntegration", "FK_AttendanceIntegration_AttendanceProvider_ProviderId", "FK_AttendanceIntegration_AttendanceVendor_VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RenameConstraint(migrationBuilder, "AttendanceIntegration", "FK_AttendanceIntegration_AttendanceVendor_VendorId", "FK_AttendanceIntegration_AttendanceProvider_ProviderId");
            RenameConstraint(migrationBuilder, "AttendanceIntegration", "FK_AttendanceIntegration_AttendancePayloadFormat_PayloadFormat~", "FK_AttendanceIntegration_AttendanceFileFormat_FileFormatId");
            RenameConstraint(migrationBuilder, "AttendanceVendorTransport", "FK_AttendanceVendorTransport_AttendanceTransportDefinition_Tra~", "FK_AttendanceProviderTransport_AttendanceTransportDefinition_T~");
            RenameConstraint(migrationBuilder, "AttendanceVendorTransport", "FK_AttendanceVendorTransport_AttendanceVendor_VendorId", "FK_AttendanceProviderTransport_AttendanceProvider_ProviderId");
            RenameConstraint(migrationBuilder, "AttendanceVendor", "FK_AttendanceVendor_AttendancePayloadFormat_DefaultPayloadForm~", "FK_AttendanceProvider_AttendanceFileFormat_DefaultFileFormatId");
            RenameConstraint(migrationBuilder, "AttendancePayloadEventCode", "FK_AttendancePayloadEventCode_AttendancePayloadFormat_PayloadF~", "FK_AttendanceFileFormatEventCode_AttendanceFileFormat_FileForm~");

            RenameConstraint(migrationBuilder, "AttendanceVendorTransport", "PK_AttendanceVendorTransport", "PK_AttendanceProviderTransport");
            RenameConstraint(migrationBuilder, "AttendanceVendor", "PK_AttendanceVendor", "PK_AttendanceProvider");
            RenameConstraint(migrationBuilder, "AttendancePayloadEventCode", "PK_AttendancePayloadEventCode", "PK_AttendanceFileFormatEventCode");
            RenameConstraint(migrationBuilder, "AttendancePayloadFormat", "PK_AttendancePayloadFormat", "PK_AttendanceFileFormat");

            migrationBuilder.RenameIndex(name: "IX_AttendanceIntegration_PayloadFormatId", schema: SchemaName, table: "AttendanceIntegration", newName: "IX_AttendanceIntegration_FileFormatId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceIntegration_VendorId", schema: SchemaName, table: "AttendanceIntegration", newName: "IX_AttendanceIntegration_ProviderId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceVendorTransport_TransportId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "IX_AttendanceProviderTransport_TransportId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceVendorTransport_TenantId_VendorId_TransportId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "IX_AttendanceProviderTransport_TenantId_ProviderId_TransportId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceVendorTransport_VendorId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "IX_AttendanceProviderTransport_ProviderId");
            migrationBuilder.RenameIndex(name: "IX_AttendanceVendor_TenantId_VendorKey", schema: SchemaName, table: "AttendanceVendor", newName: "IX_AttendanceProvider_TenantId_ProviderKey");
            migrationBuilder.RenameIndex(name: "IX_AttendanceVendor_DefaultPayloadFormatId", schema: SchemaName, table: "AttendanceVendor", newName: "IX_AttendanceProvider_DefaultFileFormatId");
            migrationBuilder.RenameIndex(name: "IX_AttendancePayloadEventCode_TenantId_PayloadFormatId_Code", schema: SchemaName, table: "AttendancePayloadEventCode", newName: "IX_AttendanceFileFormatEventCode_TenantId_FileFormatId_Code");
            migrationBuilder.RenameIndex(name: "IX_AttendancePayloadEventCode_PayloadFormatId", schema: SchemaName, table: "AttendancePayloadEventCode", newName: "IX_AttendanceFileFormatEventCode_FileFormatId");
            migrationBuilder.RenameIndex(name: "IX_AttendancePayloadFormat_TenantId_Name", schema: SchemaName, table: "AttendancePayloadFormat", newName: "IX_AttendanceFileFormat_TenantId_Name");

            migrationBuilder.RenameColumn(name: "PayloadFormatId", schema: SchemaName, table: "AttendanceIntegration", newName: "FileFormatId");
            migrationBuilder.RenameColumn(name: "VendorId", schema: SchemaName, table: "AttendanceIntegration", newName: "ProviderId");
            migrationBuilder.RenameColumn(name: "UsesPayloadFormat", schema: SchemaName, table: "AttendanceTransportDefinition", newName: "UsesFileFormat");
            migrationBuilder.RenameColumn(name: "VendorId", schema: SchemaName, table: "AttendanceVendorTransport", newName: "ProviderId");
            migrationBuilder.RenameColumn(name: "DefaultPayloadFormatId", schema: SchemaName, table: "AttendanceVendor", newName: "DefaultFileFormatId");
            migrationBuilder.RenameColumn(name: "VendorKey", schema: SchemaName, table: "AttendanceVendor", newName: "ProviderKey");
            migrationBuilder.RenameColumn(name: "PayloadFormatId", schema: SchemaName, table: "AttendancePayloadEventCode", newName: "FileFormatId");
            migrationBuilder.RenameColumn(name: "PayloadKind", schema: SchemaName, table: "AttendancePayloadFormat", newName: "FileKind");

            migrationBuilder.RenameTable(name: "AttendanceVendorTransport", schema: SchemaName, newName: "AttendanceProviderTransport", newSchema: SchemaName);
            migrationBuilder.RenameTable(name: "AttendanceVendor", schema: SchemaName, newName: "AttendanceProvider", newSchema: SchemaName);
            migrationBuilder.RenameTable(name: "AttendancePayloadEventCode", schema: SchemaName, newName: "AttendanceFileFormatEventCode", newSchema: SchemaName);
            migrationBuilder.RenameTable(name: "AttendancePayloadFormat", schema: SchemaName, newName: "AttendanceFileFormat", newSchema: SchemaName);
        }

        // Renames a constraint when it still has the old name: Npgsql may already have renamed a primary key with its table,
        // and a database created from a model snapshot may use the new names from the start.
        private static void RenameConstraint(MigrationBuilder migrationBuilder, string table, string from, string to) =>
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{from}' AND conrelid = '"{SchemaName}"."{table}"'::regclass) THEN
                        ALTER TABLE "{SchemaName}"."{table}" RENAME CONSTRAINT "{from}" TO "{to}";
                    END IF;
                END $$;
                """);
    }
}
