using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Transport",
                schema: "Attendance",
                table: "AttendanceIntegration",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "FileUpload");

            // Existing integrations keep working as before: an upload integration stays one; one whose provider
            // cannot take uploads gets the first transport the provider supports.
            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceIntegration" AS i
                SET "Transport" = CASE
                    WHEN p."SupportedTransports" LIKE '%Webhook%' THEN 'Webhook'
                    WHEN p."SupportedTransports" LIKE '%RestApi%' THEN 'RestApi'
                    WHEN p."SupportedTransports" LIKE '%DeviceSdk%' THEN 'DeviceSdk'
                    WHEN p."SupportedTransports" LIKE '%Sftp%' THEN 'Sftp'
                    WHEN p."SupportedTransports" LIKE '%Database%' THEN 'Database'
                    ELSE 'FileUpload' END
                FROM "Attendance"."AttendanceProvider" AS p
                WHERE p."Id" = i."ProviderId" AND p."SupportedTransports" NOT LIKE '%FileUpload%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Transport",
                schema: "Attendance",
                table: "AttendanceIntegration");
        }
    }
}
