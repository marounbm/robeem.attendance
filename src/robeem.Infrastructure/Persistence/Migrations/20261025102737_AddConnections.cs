using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                schema: "Attendance",
                table: "AttendanceDevice",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceConnection",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntegrationId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Transport = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AuthMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Settings = table.Column<string>(type: "text", nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PollingSchedule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceConnection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceConnection_AttendanceIntegration_IntegrationId",
                        column: x => x.IntegrationId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceIntegration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Every integration gets one default connection carrying what it had: its transport, its secret reference and its
            // schedule. The authentication method is not stored anywhere before, so it is derived: BioStar's REST API logs in for a
            // session, a webhook is signed, and every other connection is left at None for an administrator to choose on its next edit.
            // The secret is only a reference, so it moves unchanged and nothing in the secret store changes. A BioStar base URL stays in
            // its secret until the connection is edited (the adapter reads the setting first, the secret after).
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceConnection"
                    ("TenantId", "IntegrationId", "Name", "IsDefault", "Transport", "AuthMethod", "Settings", "SecretRef", "PollingSchedule",
                     "IsActive", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT i."TenantId", i."Id", 'Default', TRUE, i."Transport",
                       CASE
                           WHEN i."Transport" = 'Webhook' THEN 'HmacSignature'
                           WHEN i."Transport" = 'RestApi' AND c."AdapterKey" = 'suprema-biostar' THEN 'SessionLogin'
                           ELSE 'None'
                       END,
                       '{}', i."ConnectionConfigRef", i."PollingScheduleCron",
                       TRUE, LOCALTIMESTAMP, 0, LOCALTIMESTAMP, 0, gen_random_uuid()
                FROM "Attendance"."AttendanceIntegration" i
                LEFT JOIN "Attendance"."AttendanceConnectorDefinition" c ON c."Id" = i."ConnectorId";
                """);

            migrationBuilder.DropColumn(
                name: "ConnectionConfigRef",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "PollingScheduleCron",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.DropColumn(
                name: "Transport",
                schema: "Attendance",
                table: "AttendanceIntegration");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProcessingBatch_ConnectionId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceProcessingBatch_PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                column: "PayloadFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDevice_ConnectionId",
                schema: "Attendance",
                table: "AttendanceDevice",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnection_IntegrationId",
                schema: "Attendance",
                table: "AttendanceConnection",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnection_TenantId_IntegrationId_Default",
                schema: "Attendance",
                table: "AttendanceConnection",
                columns: new[] { "TenantId", "IntegrationId" },
                unique: true,
                filter: "\"IsDefault\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnection_TenantId_IntegrationId_Name",
                schema: "Attendance",
                table: "AttendanceConnection",
                columns: new[] { "TenantId", "IntegrationId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceDevice_AttendanceConnection_ConnectionId",
                schema: "Attendance",
                table: "AttendanceDevice",
                column: "ConnectionId",
                principalSchema: "Attendance",
                principalTable: "AttendanceConnection",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceProcessingBatch_AttendanceConnection_ConnectionId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                column: "ConnectionId",
                principalSchema: "Attendance",
                principalTable: "AttendanceConnection",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceProcessingBatch_AttendancePayloadFormat_PayloadFo~",
                schema: "Attendance",
                table: "AttendanceProcessingBatch",
                column: "PayloadFormatId",
                principalSchema: "Attendance",
                principalTable: "AttendancePayloadFormat",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionConfigRef",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PollingScheduleCron",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transport",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "FileUpload");

            // Back to one transport, secret and schedule per integration: its default connection's. A second connection, a device's
            // choice of connection and the authentication method have nowhere to go and are lost.
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceIntegration" i
                SET "Transport" = c."Transport", "ConnectionConfigRef" = c."SecretRef", "PollingScheduleCron" = c."PollingSchedule"
                FROM "Attendance"."AttendanceConnection" c
                WHERE c."IntegrationId" = i."Id" AND c."IsDefault";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceDevice_AttendanceConnection_ConnectionId",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceProcessingBatch_AttendanceConnection_ConnectionId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceProcessingBatch_AttendancePayloadFormat_PayloadFo~",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropTable(
                name: "AttendanceConnection",
                schema: "Attendance");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceProcessingBatch_ConnectionId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceProcessingBatch_PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceDevice_ConnectionId",
                schema: "Attendance",
                table: "AttendanceDevice");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropColumn(
                name: "PayloadFormatId",
                schema: "Attendance",
                table: "AttendanceProcessingBatch");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                schema: "Attendance",
                table: "AttendanceDevice");
        }
    }
}
