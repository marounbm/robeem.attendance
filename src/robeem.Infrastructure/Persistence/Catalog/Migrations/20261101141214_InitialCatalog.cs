using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Catalog");

            migrationBuilder.CreateTable(
                name: "AttendanceDeviceType",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDeviceType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceTransportDefinition",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransportKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsPull = table.Column<bool>(type: "boolean", nullable: false),
                    UsesPayloadFormat = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceTransportDefinition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceVendor",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VendorKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Manufacturer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceVendor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceConnectorDefinition",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
                        name: "FK_AttendanceConnectorDefinition_AttendanceVendor_VendorId",
                        column: x => x.VendorId,
                        principalSchema: "Catalog",
                        principalTable: "AttendanceVendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceConnectorTransport",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectorId = table.Column<int>(type: "integer", nullable: false),
                    TransportId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceConnectorTransport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceConnectorTransport_AttendanceConnectorDefinition_~",
                        column: x => x.ConnectorId,
                        principalSchema: "Catalog",
                        principalTable: "AttendanceConnectorDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceConnectorTransport_AttendanceTransportDefinition_~",
                        column: x => x.TransportId,
                        principalSchema: "Catalog",
                        principalTable: "AttendanceTransportDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendancePayloadFormat",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SupersedesId = table.Column<int>(type: "integer", nullable: true),
                    ConnectorId = table.Column<int>(type: "integer", nullable: true),
                    PayloadKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Delimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    HasHeaderRow = table.Column<bool>(type: "boolean", nullable: false),
                    SheetNumber = table.Column<int>(type: "integer", nullable: false),
                    RecordPath = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
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
                    DateFormats = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TimeFormats = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TimestampCulture = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayloadColumns = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePayloadFormat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendancePayloadFormat_AttendanceConnectorDefinition_Conne~",
                        column: x => x.ConnectorId,
                        principalSchema: "Catalog",
                        principalTable: "AttendanceConnectorDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendancePayloadFormat_AttendancePayloadFormat_SupersedesId",
                        column: x => x.SupersedesId,
                        principalSchema: "Catalog",
                        principalTable: "AttendancePayloadFormat",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendancePayloadEventCode",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PayloadFormatId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AttendancePayloadEventCode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendancePayloadEventCode_AttendancePayloadFormat_PayloadF~",
                        column: x => x.PayloadFormatId,
                        principalSchema: "Catalog",
                        principalTable: "AttendancePayloadFormat",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorDefinition_ConnectorKey",
                schema: "Catalog",
                table: "AttendanceConnectorDefinition",
                column: "ConnectorKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorDefinition_DefaultPayloadFormatId",
                schema: "Catalog",
                table: "AttendanceConnectorDefinition",
                column: "DefaultPayloadFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorDefinition_VendorId",
                schema: "Catalog",
                table: "AttendanceConnectorDefinition",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorTransport_ConnectorId_TransportId",
                schema: "Catalog",
                table: "AttendanceConnectorTransport",
                columns: new[] { "ConnectorId", "TransportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConnectorTransport_TransportId",
                schema: "Catalog",
                table: "AttendanceConnectorTransport",
                column: "TransportId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDeviceType_Name",
                schema: "Catalog",
                table: "AttendanceDeviceType",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadEventCode_PayloadFormatId_Code",
                schema: "Catalog",
                table: "AttendancePayloadEventCode",
                columns: new[] { "PayloadFormatId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadFormat_ConnectorId",
                schema: "Catalog",
                table: "AttendancePayloadFormat",
                column: "ConnectorId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadFormat_Name_Version",
                schema: "Catalog",
                table: "AttendancePayloadFormat",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePayloadFormat_SupersedesId",
                schema: "Catalog",
                table: "AttendancePayloadFormat",
                column: "SupersedesId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceTransportDefinition_TransportKey",
                schema: "Catalog",
                table: "AttendanceTransportDefinition",
                column: "TransportKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceVendor_VendorKey",
                schema: "Catalog",
                table: "AttendanceVendor",
                column: "VendorKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceConnectorDefinition_AttendancePayloadFormat_Defau~",
                schema: "Catalog",
                table: "AttendanceConnectorDefinition",
                column: "DefaultPayloadFormatId",
                principalSchema: "Catalog",
                principalTable: "AttendancePayloadFormat",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceConnectorDefinition_AttendancePayloadFormat_Defau~",
                schema: "Catalog",
                table: "AttendanceConnectorDefinition");

            migrationBuilder.DropTable(
                name: "AttendanceConnectorTransport",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "AttendanceDeviceType",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "AttendancePayloadEventCode",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "AttendanceTransportDefinition",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "AttendancePayloadFormat",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "AttendanceConnectorDefinition",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "AttendanceVendor",
                schema: "Catalog");
        }
    }
}
