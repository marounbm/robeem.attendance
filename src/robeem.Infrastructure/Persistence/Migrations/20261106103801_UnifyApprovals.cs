using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace robeem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceApproval",
                schema: "Attendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Workflow = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByEmployeeId = table.Column<int>(type: "integer", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ManagerDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ManagerDecidedById = table.Column<int>(type: "integer", nullable: true),
                    ManagerDecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    HrDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HrDecidedById = table.Column<int>(type: "integer", nullable: true),
                    HrDecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AttendanceRecordId = table.Column<int>(type: "integer", nullable: true),
                    OvertimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: false),
                    GUID = table.Column<Guid>(type: "uuid", nullable: false),

                    // Only for the data step below, which ties each request's edits to its approval; dropped before the end.
                    MigrationKey = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceApproval", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceApproval_AttendanceRecord_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalSchema: "Attendance",
                        principalTable: "AttendanceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceApproval_Profile_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceApproval_Profile_HrDecidedById",
                        column: x => x.HrDecidedById,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceApproval_Profile_ManagerDecidedById",
                        column: x => x.ManagerDecidedById,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceApproval_Profile_RequestedByEmployeeId",
                        column: x => x.RequestedByEmployeeId,
                        principalSchema: "HR",
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<int>(
                name: "AttendanceApprovalId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPending",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 1. Every correction request becomes one approval. A request is the rows that share a RequestId, and they were always decided
            // together, so the first row says where the whole request stood. A status of Approved was only ever the moment before Applied
            // inside one transaction; if one was stored it is still waiting. The workflow was copied from the policy when it was proposed,
            // and is kept as it was.
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceApproval"
                    ("TenantId", "Type", "EmployeeId", "AttendanceDate", "Status", "Workflow", "RequestedByEmployeeId", "RequestedAt",
                     "ManagerDecision", "ManagerDecidedById", "ManagerDecidedAt", "HrDecision", "HrDecidedById", "HrDecidedAt",
                     "Reason", "AppliedAt", "AttendanceRecordId", "OvertimeMinutes",
                     "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID", "MigrationKey")
                SELECT DISTINCT ON (c."TenantId", c."RequestId")
                    c."TenantId", 'Correction', c."EmployeeId", c."AttendanceDate",
                    CASE c."Status" WHEN 'Approved' THEN 'Pending' ELSE c."Status" END,
                    c."ApprovalWorkflow", c."RequestedByEmployeeId", c."RequestedAt",
                    c."ManagerDecision", c."ManagerDecidedById", c."ManagerDecidedAt", c."HrDecision", c."HrDecidedById", c."HrDecidedAt",
                    NULL, c."AppliedAt", NULL, 0,
                    c."CreatedOn", c."CreatedBy", c."ModifiedOn", c."ModifiedBy", gen_random_uuid(),
                    c."TenantId" || ':' || c."RequestId"::text
                FROM "Attendance"."AttendanceCorrection" c
                ORDER BY c."TenantId", c."RequestId", c."Id";
                """);

            migrationBuilder.Sql("""
                UPDATE "Attendance"."AttendanceCorrection" c
                SET "AttendanceApprovalId" = a."Id",
                    "IsPending" = c."Status" IN ('Pending', 'Approved')
                FROM "Attendance"."AttendanceApproval" a
                WHERE a."MigrationKey" = c."TenantId" || ':' || c."RequestId"::text;
                """);

            // 2. Every review decision becomes a decided review approval of the same record version. A review was one step, by the manager
            // or by HR: the step that was taken fills its slot. When it was queued is not known, so the decision time stands for it.
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceApproval"
                    ("TenantId", "Type", "EmployeeId", "AttendanceDate", "Status", "Workflow", "RequestedByEmployeeId", "RequestedAt",
                     "ManagerDecision", "ManagerDecidedById", "ManagerDecidedAt", "HrDecision", "HrDecidedById", "HrDecidedAt",
                     "Reason", "AppliedAt", "AttendanceRecordId", "OvertimeMinutes",
                     "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT
                    d."TenantId", 'Review', d."EmployeeId", d."AttendanceDate", d."Outcome", 'ManagerOrHr', NULL, d."DecidedAt",
                    CASE WHEN d."DecidedByRole" = 'Manager' THEN d."Outcome" END,
                    CASE WHEN d."DecidedByRole" = 'Manager' THEN d."DecidedById" END,
                    CASE WHEN d."DecidedByRole" = 'Manager' THEN d."DecidedAt" END,
                    CASE WHEN d."DecidedByRole" = 'HR' THEN d."Outcome" END,
                    CASE WHEN d."DecidedByRole" = 'HR' THEN d."DecidedById" END,
                    CASE WHEN d."DecidedByRole" = 'HR' THEN d."DecidedAt" END,
                    d."Reason", NULL, d."AttendanceRecordId", d."OvertimeMinutes",
                    d."CreatedOn", d."CreatedBy", d."ModifiedOn", d."ModifiedBy", gen_random_uuid()
                FROM "Attendance"."AttendanceReviewDecision" d;
                """);

            // 3. Every record that is waiting for review now has its pending review approval: the queue used to be these records.
            migrationBuilder.Sql("""
                INSERT INTO "Attendance"."AttendanceApproval"
                    ("TenantId", "Type", "EmployeeId", "AttendanceDate", "Status", "Workflow", "RequestedByEmployeeId", "RequestedAt",
                     "Reason", "AppliedAt", "AttendanceRecordId", "OvertimeMinutes",
                     "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "GUID")
                SELECT
                    r."TenantId", 'Review', r."EmployeeId", r."AttendanceDate", 'Pending', 'ManagerOrHr', NULL, r."CreatedOn",
                    NULL, NULL, r."Id", r."OvertimeMinutes",
                    r."CreatedOn", r."CreatedBy", r."ModifiedOn", r."ModifiedBy", gen_random_uuid()
                FROM "Attendance"."AttendanceRecord" r
                WHERE r."IsCurrent" AND r."Status" = 'PendingReview';
                """);

            migrationBuilder.DropColumn(
                name: "MigrationKey",
                schema: "Attendance",
                table: "AttendanceApproval");

            migrationBuilder.AlterColumn<int>(
                name: "AttendanceApprovalId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // The decisions and the status now live in the approval.
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrection_Profile_HrDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceCorrection_Profile_ManagerDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropTable(
                name: "AttendanceReviewDecision",
                schema: "Attendance");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_HrDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_ManagerDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_TenantId_AttendanceExceptionId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_TenantId_RequestId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceCorrection_TenantId_Status",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "ApprovalWorkflow",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "HrDecidedAt",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "HrDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "HrDecision",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "ManagerDecidedAt",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "ManagerDecidedById",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "ManagerDecision",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "RequestId",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "Attendance",
                table: "AttendanceCorrection");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_AttendanceApprovalId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "AttendanceApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_TenantId_AttendanceApprovalId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                columns: new[] { "TenantId", "AttendanceApprovalId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrection_TenantId_AttendanceExceptionId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                columns: new[] { "TenantId", "AttendanceExceptionId" },
                unique: true,
                filter: "\"IsPending\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_AttendanceRecordId",
                schema: "Attendance",
                table: "AttendanceApproval",
                column: "AttendanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_EmployeeId",
                schema: "Attendance",
                table: "AttendanceApproval",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_HrDecidedById",
                schema: "Attendance",
                table: "AttendanceApproval",
                column: "HrDecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_ManagerDecidedById",
                schema: "Attendance",
                table: "AttendanceApproval",
                column: "ManagerDecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_RequestedByEmployeeId",
                schema: "Attendance",
                table: "AttendanceApproval",
                column: "RequestedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_TenantId_AttendanceRecordId",
                schema: "Attendance",
                table: "AttendanceApproval",
                columns: new[] { "TenantId", "AttendanceRecordId" },
                unique: true,
                filter: "\"AttendanceRecordId\" IS NOT NULL AND \"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceApproval_TenantId_Status_Type_AttendanceDate",
                schema: "Attendance",
                table: "AttendanceApproval",
                columns: new[] { "TenantId", "Status", "Type", "AttendanceDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceCorrection_AttendanceApproval_AttendanceApprovalId",
                schema: "Attendance",
                table: "AttendanceCorrection",
                column: "AttendanceApprovalId",
                principalSchema: "Attendance",
                principalTable: "AttendanceApproval",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Like MoveCatalogToPrime, this cannot be undone: a review decision and a correction request would have to be split back into
            // their old shapes, and what the engine queued since has no old form. Restore the backup taken before it ran.
            throw new NotSupportedException("UnifyApprovals cannot be undone. Restore the database from the backup taken before it was applied.");
        }
    }
}
