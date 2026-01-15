using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    public partial class AddMediaCleanupRunLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- PageSections hardening (safe across environments) ---
            // Ensure new canonical column exists
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PageSections', N'SettingsJson') IS NULL
BEGIN
    ALTER TABLE dbo.PageSections ADD SettingsJson nvarchar(max) NULL;
END
");

            // Drop legacy ContentJson if present
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PageSections', N'ContentJson') IS NOT NULL
BEGIN
    DECLARE @dc sysname;
    SELECT @dc = d.name
    FROM sys.default_constraints d
    INNER JOIN sys.columns c
        ON d.parent_object_id = c.object_id
       AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID(N'dbo.PageSections')
      AND c.name = N'ContentJson';

    IF @dc IS NOT NULL
    BEGIN
        DECLARE @sql nvarchar(max) =
            N'ALTER TABLE dbo.PageSections DROP CONSTRAINT ' + QUOTENAME(@dc) + N';';
        EXEC sys.sp_executesql @sql;
    END

    ALTER TABLE dbo.PageSections DROP COLUMN ContentJson;
END
");


            // Drop legacy TypeKey if present
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PageSections', N'TypeKey') IS NOT NULL
BEGIN
    DECLARE @dc2 sysname;
    SELECT @dc2 = d.name
    FROM sys.default_constraints d
    INNER JOIN sys.columns c
        ON d.parent_object_id = c.object_id
       AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID(N'dbo.PageSections')
      AND c.name = N'TypeKey';

    IF @dc2 IS NOT NULL
    BEGIN
        DECLARE @sql2 nvarchar(max) =
            N'ALTER TABLE dbo.PageSections DROP CONSTRAINT ' + QUOTENAME(@dc2) + N';';
        EXEC sys.sp_executesql @sql2;
    END

    ALTER TABLE dbo.PageSections DROP COLUMN TypeKey;
END
");

            // --- MediaAssets (correct schema types) ---
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),

                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),

                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),

                    // FIX: use bigint
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),

                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),

                    // FIX: nullable thumbnail
                    ThumbStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),

                    // FIX: numeric dimensions
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),

                    // FIX: typically optional
                    AltText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),

                    CheckSum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),

                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),

                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),

                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            // --- MediaCleanupRunLogs (unchanged) ---
            migrationBuilder.CreateTable(
                name: "MediaCleanupRunLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    RunType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    BatchSize = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EligibleCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedCount = table.Column<int>(type: "int", nullable: false),
                    DeletedOriginalFilesCount = table.Column<int>(type: "int", nullable: false),
                    DeletedThumbnailFilesCount = table.Column<int>(type: "int", nullable: false),
                    HardDeletedDbRowsCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ErrorSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaCleanupRunLogs", x => x.Id);
                });

            // --- Indexes (keep yours; adjust nullable index ok) ---
            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ContentType",
                table: "MediaAssets",
                column: "ContentType");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_FileName",
                table: "MediaAssets",
                column: "FileName");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_StorageKey",
                table: "MediaAssets",
                column: "StorageKey");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_TenantId_CheckSum",
                table: "MediaAssets",
                columns: new[] { "TenantId", "CheckSum" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_TenantId_IsDeleted",
                table: "MediaAssets",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ThumbStorageKey",
                table: "MediaAssets",
                column: "ThumbStorageKey");

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
            migrationBuilder.DropTable(name: "MediaAssets");
            migrationBuilder.DropTable(name: "MediaCleanupRunLogs");

            // Down: be conservative. Re-adding legacy columns is optional and does not restore data.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PageSections', N'SettingsJson') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PageSections DROP COLUMN SettingsJson;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PageSections', N'ContentJson') IS NULL
BEGIN
    ALTER TABLE dbo.PageSections ADD ContentJson nvarchar(max) NULL;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PageSections', N'TypeKey') IS NULL
BEGIN
    ALTER TABLE dbo.PageSections ADD TypeKey nvarchar(100) NULL;
END
");
        }
    }
}
