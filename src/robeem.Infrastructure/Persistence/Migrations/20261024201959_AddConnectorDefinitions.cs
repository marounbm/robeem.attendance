using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the connector layer between a vendor and what reads its data. Written by hand: the scaffold renames
    /// <c>AttendanceIntegration.VendorId</c> to <c>ConnectorId</c> in place, which would read vendor ids as connector ids.
    /// Instead every vendor gets one connector carrying its key, transports, default payload format and support flags; every
    /// integration is pointed at its vendor's connector; and only then are the columns and the table the connector replaces dropped.
    /// </summary>
    public partial class AddConnectorDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceConnectorDefinition",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    ConnectorKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AdapterKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupportsIncrementalSync = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultPayloadFormatId = table.Column<int>(type: "integer", nullable: true),
                    EncryptionKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceConnectorDefinition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceConnectorDefinition_AttendancePayloadFormat_Defau~",
                        column: x => x.DefaultPayloadFormatId,
                        principalSchema: "Attendance",
                        principalTable: "AttendancePayloadFormat",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceConnectorDefinition_AttendanceVendor_VendorId",
                        column: x => x.VendorId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceVendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceConnectorTransport",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConnectorId = table.Column<int>(type: "integer", nullable: false),
                    TransportId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceConnectorTransport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceConnectorTransport_AttendanceConnectorDefinition_~",
                        column: x => x.ConnectorId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceConnectorDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceConnectorTransport_AttendanceTransportDefinition_~",
                        column: x => x.TransportId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceTransportDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<int>(
                name: "ConnectorId",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectorId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "integer",
                nullable: true);

            // One connector for each vendor, with the vendor's key. It keeps the adapter key the vendor was read by (the vendor key),
            // except for a vendor that was read by a default payload format alone and has no adapter of its own in code.
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceConnectorDefinition"
                    ("TenantId", "VendorId", "ConnectorKey", "DisplayName", "Description", "AdapterKey", "SupportsIncrementalSync",
                     "DefaultPayloadFormatId", "EncryptionKind", "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT v."TenantId", v."Id", v."VendorKey", v."DisplayName", '',
                       CASE WHEN v."DefaultPayloadFormatId" IS NULL OR v."VendorKey" IN ('suprema-biostar', 'generic-webhook') THEN v."VendorKey" ELSE NULL END,
                       v."SupportsIncrementalSync", v."DefaultPayloadFormatId", 'None', v."IsActive", LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()
                FROM "Attendance"."AttendanceVendor" v;
                """);

            // The vendor's transports move to its connector; a vendor flagged as sending webhooks lists the Webhook transport.
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceConnectorTransport" ("TenantId", "ConnectorId", "TransportId")
                SELECT vt."TenantId", c."Id", vt."TransportId"
                FROM "Attendance"."AttendanceVendorTransport" vt
                JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."VendorId" = vt."VendorId" AND c."TenantId" = vt."TenantId";
                """);
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceConnectorTransport" ("TenantId", "ConnectorId", "TransportId")
                SELECT v."TenantId", c."Id", td."Id"
                FROM "Attendance"."AttendanceVendor" v
                JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."VendorId" = v."Id" AND c."TenantId" = v."TenantId"
                JOIN "Attendance"."AttendanceTransportDefinition" td ON td."TenantId" = v."TenantId" AND td."TransportKey" = 'Webhook'
                WHERE v."SupportsWebhooks"
                  AND NOT EXISTS (SELECT 1 FROM "Attendance"."AttendanceConnectorTransport" ct WHERE ct."ConnectorId" = c."Id" AND ct."TransportId" = td."Id");
                """);

            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceIntegration" i
                SET "ConnectorId" = c."Id"
                FROM "Attendance"."AttendanceConnectorDefinition" c
                WHERE c."VendorId" = i."VendorId" AND c."TenantId" = i."TenantId";
                """);

            // A payload format that is the default of exactly one connector is made for it; one shared by several stays shared.
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendancePayloadFormat" f
                SET "ConnectorId" = c."Id"
                FROM "Attendance"."AttendanceConnectorDefinition" c
                WHERE c."DefaultPayloadFormatId" = f."Id"
                  AND (SELECT COUNT(*) FROM "Attendance"."AttendanceConnectorDefinition" c2 WHERE c2."DefaultPayloadFormatId" = f."Id") = 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "ConnectorId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceIntegration_AttendanceVendor_VendorId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceIntegration_VendorId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "VendorId",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropTable(
                name: "AttendanceVendorTransport",
                schema: "Attendance");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceVendor_AttendancePayloadFormat_DefaultPayloadForm~",
                schema: "Attendance",
                table: "AttendanceVendor");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceVendor_DefaultPayloadFormatId",
                schema: "Attendance",
                table: "AttendanceVendor");

            migrationBuilder.DropColumn(name: "DefaultPayloadFormatId", schema: "Attendance", table: "AttendanceVendor");
            migrationBuilder.DropColumn(name: "SupportsIncrementalSync", schema: "Attendance", table: "AttendanceVendor");
            migrationBuilder.DropColumn(name: "SupportsWebhooks", schema: "Attendance", table: "AttendanceVendor");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceIntegration_ConnectorId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "ConnectorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadFormat_ConnectorId",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                column: "ConnectorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorDefinition_DefaultPayloadFormatId",
                schema: "Attendance",
                table: "AttendanceConnectorDefinition",
                column: "DefaultPayloadFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorDefinition_TenantId_ConnectorKey",
                schema: "Attendance",
                table: "AttendanceConnectorDefinition",
                columns: new[] { "TenantId", "ConnectorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorDefinition_VendorId",
                schema: "Attendance",
                table: "AttendanceConnectorDefinition",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorTransport_ConnectorId",
                schema: "Attendance",
                table: "AttendanceConnectorTransport",
                column: "ConnectorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorTransport_TenantId_ConnectorId_Transport~",
                schema: "Attendance",
                table: "AttendanceConnectorTransport",
                columns: new[] { "TenantId", "ConnectorId", "TransportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorTransport_TransportId",
                schema: "Attendance",
                table: "AttendanceConnectorTransport",
                column: "TransportId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceIntegration_AttendanceConnectorDefinition_Connect~",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "ConnectorId",
                principalSchema: "Attendance",
                principalTable: "AttendanceConnectorDefinition",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendancePayloadFormat_AttendanceConnectorDefinition_Conne~",
                schema: "Attendance",
                table: "AttendancePayloadFormat",
                column: "ConnectorId",
                principalSchema: "Attendance",
                principalTable: "AttendanceConnectorDefinition",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceIntegration_AttendanceConnectorDefinition_Connect~",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendancePayloadFormat_AttendanceConnectorDefinition_Conne~",
                schema: "Attendance",
                table: "AttendancePayloadFormat");

            migrationBuilder.AddColumn<int>(name: "DefaultPayloadFormatId", schema: "Attendance", table: "AttendanceVendor", type: "integer", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "SupportsIncrementalSync", schema: "Attendance", table: "AttendanceVendor", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "SupportsWebhooks", schema: "Attendance", table: "AttendanceVendor", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "VendorId", schema: "Attendance", table: "AttendanceIntegration", type: "integer", nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceVendorTransport",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransportId = table.Column<int>(type: "integer", nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceVendorTransport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceVendorTransport_AttendanceTransportDefinition_Tra~",
                        column: x => x.TransportId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceTransportDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceVendorTransport_AttendanceVendor_VendorId",
                        column: x => x.VendorId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceVendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Best effort: a vendor with several connectors gets the first one's default format and the union of their transports.
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceIntegration" i
                SET "VendorId" = c."VendorId"
                FROM "Attendance"."AttendanceConnectorDefinition" c
                WHERE c."Id" = i."ConnectorId";
                """);
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceVendor" v
                SET "DefaultPayloadFormatId" = (SELECT c."DefaultPayloadFormatId" FROM "Attendance"."AttendanceConnectorDefinition" c WHERE c."VendorId" = v."Id" ORDER BY c."Id" LIMIT 1),
                    "SupportsIncrementalSync" = EXISTS (SELECT 1 FROM "Attendance"."AttendanceConnectorDefinition" c WHERE c."VendorId" = v."Id" AND c."SupportsIncrementalSync"),
                    "SupportsWebhooks" = EXISTS (
                        SELECT 1 FROM "Attendance"."AttendanceConnectorDefinition" c
                        JOIN "Attendance"."AttendanceConnectorTransport" ct ON ct."ConnectorId" = c."Id"
                        JOIN "Attendance"."AttendanceTransportDefinition" td ON td."Id" = ct."TransportId"
                        WHERE c."VendorId" = v."Id" AND td."TransportKey" = 'Webhook');
                """);
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceVendorTransport" ("TenantId", "VendorId", "TransportId")
                SELECT DISTINCT ct."TenantId", c."VendorId", ct."TransportId"
                FROM "Attendance"."AttendanceConnectorTransport" ct
                JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."Id" = ct."ConnectorId";
                """);

            migrationBuilder.AlterColumn<int>(
                name: "VendorId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.DropIndex(name: "IX_AttendanceIntegration_ConnectorId", schema: "Attendance", table: "AttendanceIntegration");
            migrationBuilder.DropColumn(name: "ConnectorId", schema: "Attendance", table: "AttendanceIntegration");
            migrationBuilder.DropIndex(name: "IX_AttendancePayloadFormat_ConnectorId", schema: "Attendance", table: "AttendancePayloadFormat");
            migrationBuilder.DropColumn(name: "ConnectorId", schema: "Attendance", table: "AttendancePayloadFormat");
            migrationBuilder.DropTable(name: "AttendanceConnectorTransport", schema: "Attendance");
            migrationBuilder.DropTable(name: "AttendanceConnectorDefinition", schema: "Attendance");

            migrationBuilder.CreateIndex(name: "IX_AttendanceVendor_DefaultPayloadFormatId", schema: "Attendance", table: "AttendanceVendor", column: "DefaultPayloadFormatId");
            migrationBuilder.CreateIndex(name: "IX_AttendanceIntegration_VendorId", schema: "Attendance", table: "AttendanceIntegration", column: "VendorId");
            migrationBuilder.CreateIndex(name: "IX_AttendanceVendorTransport_TenantId_VendorId_TransportId", schema: "Attendance", table: "AttendanceVendorTransport", columns: new[] { "TenantId", "VendorId", "TransportId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_AttendanceVendorTransport_TransportId", schema: "Attendance", table: "AttendanceVendorTransport", column: "TransportId");
            migrationBuilder.CreateIndex(name: "IX_AttendanceVendorTransport_VendorId", schema: "Attendance", table: "AttendanceVendorTransport", column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceVendor_AttendancePayloadFormat_DefaultPayloadForm~",
                schema: "Attendance",
                table: "AttendanceVendor",
                column: "DefaultPayloadFormatId",
                principalSchema: "Attendance",
                principalTable: "AttendancePayloadFormat",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceIntegration_AttendanceVendor_VendorId",
                schema: "Attendance",
                table: "AttendanceIntegration",
                column: "VendorId",
                principalSchema: "Attendance",
                principalTable: "AttendanceVendor",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
