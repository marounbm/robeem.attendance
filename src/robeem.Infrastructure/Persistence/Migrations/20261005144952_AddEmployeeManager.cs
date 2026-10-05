using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ManagerId",
                schema: "HR",
                table: "Profile",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profile_ManagerId",
                schema: "HR",
                table: "Profile",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Profile_TenantId_ManagerId",
                schema: "HR",
                table: "Profile",
                columns: new[] { "TenantId", "ManagerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Profile_Profile_ManagerId",
                schema: "HR",
                table: "Profile",
                column: "ManagerId",
                principalSchema: "HR",
                principalTable: "Profile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Profile_Profile_ManagerId",
                schema: "HR",
                table: "Profile");

            migrationBuilder.DropIndex(
                name: "IX_Profile_ManagerId",
                schema: "HR",
                table: "Profile");

            migrationBuilder.DropIndex(
                name: "IX_Profile_TenantId_ManagerId",
                schema: "HR",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                schema: "HR",
                table: "Profile");
        }
    }
}
