using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves the mapping of a vendor's employee id to an employee from the integration to the device. EF Core scaffolds this as a
    /// column rename, which would read integration ids as device ids; it is written by hand to carry the data instead.
    /// </summary>
    public partial class MoveEmployeeMappingToDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExternalEmployeeIdMapping_AttendanceIntegration_Integration~",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.DropIndex(
                name: "IX_ExternalEmployeeIdMapping_TenantId_IntegrationId_ExternalEm~",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.DropIndex(
                name: "IX_ExternalEmployeeIdMapping_IntegrationId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.AddColumn<int>(
                name: "DeviceId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                type: "integer",
                nullable: true);

            // A mapping that applied to the whole integration now applies to each of its devices: the first device takes the
            // existing row, every other device gets a copy.
            migrationBuilder.Sql(
                """
                UPDATE "Attendance"."ExternalEmployeeIdMapping" m
                SET "DeviceId" = (SELECT MIN(d."Id") FROM "Attendance"."AttendanceDevice" d WHERE d."IntegrationId" = m."IntegrationId");
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "Attendance"."ExternalEmployeeIdMapping"
                    ("TenantId", "IntegrationId", "ExternalEmployeeId", "EmployeeId", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID", "DeviceId")
                SELECT m."TenantId", m."IntegrationId", m."ExternalEmployeeId", m."EmployeeId", m."CreatedOn", m."CreatedBy", m."ModifiedOn", m."ModifiedBy", gen_random_uuid(), d."Id"
                FROM "Attendance"."ExternalEmployeeIdMapping" m
                JOIN "Attendance"."AttendanceDevice" d ON d."IntegrationId" = m."IntegrationId" AND d."Id" <> m."DeviceId"
                WHERE m."DeviceId" IS NOT NULL;
                """);

            // An integration with no device has no punch that resolved to an employee through it (a punch of an unknown device is
            // flagged), so there is no device to move its mappings to.
            migrationBuilder.Sql("""DELETE FROM "Attendance"."ExternalEmployeeIdMapping" WHERE "DeviceId" IS NULL;""");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.AlterColumn<int>(
                name: "DeviceId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeIdMapping_DeviceId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeIdMapping_TenantId_DeviceId_ExternalEmploye~",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                columns: new[] { "TenantId", "DeviceId", "ExternalEmployeeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExternalEmployeeIdMapping_AttendanceDevice_DeviceId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                column: "DeviceId",
                principalSchema: "Attendance",
                principalTable: "AttendanceDevice",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExternalEmployeeIdMapping_AttendanceDevice_DeviceId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.DropIndex(
                name: "IX_ExternalEmployeeIdMapping_TenantId_DeviceId_ExternalEmploye~",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.DropIndex(
                name: "IX_ExternalEmployeeIdMapping_DeviceId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.AddColumn<int>(
                name: "IntegrationId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Attendance"."ExternalEmployeeIdMapping" m
                SET "IntegrationId" = (SELECT d."IntegrationId" FROM "Attendance"."AttendanceDevice" d WHERE d."Id" = m."DeviceId");
                """);

            // Back at integration level an id can be mapped once: where devices disagreed, the oldest mapping stays.
            migrationBuilder.Sql(
                """
                DELETE FROM "Attendance"."ExternalEmployeeIdMapping" m
                USING "Attendance"."ExternalEmployeeIdMapping" older
                WHERE m."Id" > older."Id"
                  AND m."TenantId" = older."TenantId"
                  AND m."IntegrationId" = older."IntegrationId"
                  AND m."ExternalEmployeeId" = older."ExternalEmployeeId";
                """);

            migrationBuilder.DropColumn(
                name: "DeviceId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping");

            migrationBuilder.AlterColumn<int>(
                name: "IntegrationId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeIdMapping_IntegrationId",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeIdMapping_TenantId_IntegrationId_ExternalEm~",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                columns: new[] { "TenantId", "IntegrationId", "ExternalEmployeeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExternalEmployeeIdMapping_AttendanceIntegration_Integration~",
                schema: "Attendance",
                table: "ExternalEmployeeIdMapping",
                column: "IntegrationId",
                principalSchema: "Attendance",
                principalTable: "AttendanceIntegration",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
