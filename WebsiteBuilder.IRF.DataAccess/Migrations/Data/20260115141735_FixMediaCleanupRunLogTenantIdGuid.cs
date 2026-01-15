using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    public partial class FixMediaCleanupRunLogTenantIdGuid : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop indexes that reference TenantId (int)
            migrationBuilder.DropIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_StartedAtUtc",
                table: "MediaCleanupRunLogs");

            migrationBuilder.DropIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_Status_StartedAtUtc",
                table: "MediaCleanupRunLogs");

            // 1) Add new GUID column (nullable for backfill)
            migrationBuilder.AddColumn<Guid>(
                name: "TenantIdGuid",
                table: "MediaCleanupRunLogs",
                type: "uniqueidentifier",
                nullable: true);

            // 2) Backfill strategy:
            // If this environment is effectively single-tenant, assign ALL existing rows to that tenant.
            // Adjust if you have a different mapping rule.
            migrationBuilder.Sql(@"
                DECLARE @tenant uniqueidentifier = (SELECT TOP (1) [Id] FROM [Tenants] ORDER BY [CreatedAt] ASC);

                UPDATE [MediaCleanupRunLogs]
                SET [TenantIdGuid] = @tenant
                WHERE [TenantIdGuid] IS NULL;
            ");

            // 3) Drop old int column
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MediaCleanupRunLogs");

            // 4) Rename TenantIdGuid -> TenantId and make it NOT NULL
            migrationBuilder.RenameColumn(
                name: "TenantIdGuid",
                table: "MediaCleanupRunLogs",
                newName: "TenantId");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "MediaCleanupRunLogs",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            // Recreate indexes
            migrationBuilder.CreateIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_StartedAtUtc",
                table: "MediaCleanupRunLogs",
                columns: new[] { "TenantId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_Status_StartedAtUtc",
                table: "MediaCleanupRunLogs",
                columns: new[] { "TenantId", "Status", "StartedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: drop indexes
            migrationBuilder.DropIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_StartedAtUtc",
                table: "MediaCleanupRunLogs");

            migrationBuilder.DropIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_Status_StartedAtUtc",
                table: "MediaCleanupRunLogs");

            // Add old int column back (default 0)
            migrationBuilder.AddColumn<int>(
                name: "TenantIdInt",
                table: "MediaCleanupRunLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Drop GUID TenantId
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MediaCleanupRunLogs");

            // Rename TenantIdInt -> TenantId
            migrationBuilder.RenameColumn(
                name: "TenantIdInt",
                table: "MediaCleanupRunLogs",
                newName: "TenantId");

            // Recreate indexes (int version)
            migrationBuilder.CreateIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_StartedAtUtc",
                table: "MediaCleanupRunLogs",
                columns: new[] { "TenantId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaCleanupRunLogs_TenantId_Status_StartedAtUtc",
                table: "MediaCleanupRunLogs",
                columns: new[] { "TenantId", "Status", "StartedAtUtc" });
        }
    }
}
