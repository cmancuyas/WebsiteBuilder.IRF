using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class RemoveLegacyPageSectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ------------------------------------------------------------
            // 1) Create SectionTypes (if not already created by earlier migration)
            // ------------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "SectionTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SectionTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SectionTypes_Name",
                table: "SectionTypes",
                column: "Name",
                unique: true);

            // ------------------------------------------------------------
            // 2) Seed SectionTypes from existing legacy TypeKey values
            //    - Pull from PageSections.TypeKey and PageRevisionSections.TypeKey if present
            // ------------------------------------------------------------
            migrationBuilder.Sql(@"
;WITH keys AS (
    SELECT DISTINCT LTRIM(RTRIM(TypeKey)) AS TypeKey
    FROM dbo.PageSections
    WHERE COL_LENGTH('dbo.PageSections','TypeKey') IS NOT NULL
      AND TypeKey IS NOT NULL AND LTRIM(RTRIM(TypeKey)) <> ''

    UNION

    SELECT DISTINCT LTRIM(RTRIM(TypeKey)) AS TypeKey
    FROM dbo.PageRevisionSections
    WHERE COL_LENGTH('dbo.PageRevisionSections','TypeKey') IS NOT NULL
      AND TypeKey IS NOT NULL AND LTRIM(RTRIM(TypeKey)) <> ''
)
INSERT INTO dbo.SectionTypes([Name])
SELECT k.TypeKey
FROM keys k
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.SectionTypes st WHERE st.[Name] = k.TypeKey
);
");

            // Optional: ensure at least "Text" exists (useful default)
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM dbo.SectionTypes WHERE [Name] = 'Text')
BEGIN
    INSERT INTO dbo.SectionTypes([Name]) VALUES ('Text');
END
");

            // ------------------------------------------------------------
            // 3) Add SectionTypeId columns as NULLABLE first (critical!)
            // ------------------------------------------------------------
            migrationBuilder.AddColumn<int>(
                name: "SectionTypeId",
                table: "PageSections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SectionTypeId",
                table: "PageRevisionSections",
                type: "int",
                nullable: true);

            // ------------------------------------------------------------
            // 4) Backfill SettingsJson from ContentJson (only where SettingsJson is empty)
            // ------------------------------------------------------------
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageSections','ContentJson') IS NOT NULL
BEGIN
    UPDATE ps
    SET ps.SettingsJson = ps.ContentJson
    FROM dbo.PageSections ps
    WHERE (ps.SettingsJson IS NULL OR LTRIM(RTRIM(ps.SettingsJson)) = '')
      AND ps.ContentJson IS NOT NULL
      AND LTRIM(RTRIM(ps.ContentJson)) <> '';
END
");

            // Ensure SettingsJson is not null/empty
            migrationBuilder.Sql(@"
UPDATE ps
SET ps.SettingsJson = '{}'
FROM dbo.PageSections ps
WHERE ps.SettingsJson IS NULL OR LTRIM(RTRIM(ps.SettingsJson)) = '';
");

            // ------------------------------------------------------------
            // 5) Backfill SectionTypeId from legacy TypeKey -> SectionTypes.Name
            // ------------------------------------------------------------
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageSections','TypeKey') IS NOT NULL
BEGIN
    UPDATE ps
    SET ps.SectionTypeId = st.Id
    FROM dbo.PageSections ps
    INNER JOIN dbo.SectionTypes st
        ON st.[Name] = LTRIM(RTRIM(ps.TypeKey))
    WHERE ps.SectionTypeId IS NULL
      AND ps.TypeKey IS NOT NULL
      AND LTRIM(RTRIM(ps.TypeKey)) <> '';
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageRevisionSections','TypeKey') IS NOT NULL
BEGIN
    UPDATE prs
    SET prs.SectionTypeId = st.Id
    FROM dbo.PageRevisionSections prs
    INNER JOIN dbo.SectionTypes st
        ON st.[Name] = LTRIM(RTRIM(prs.TypeKey))
    WHERE prs.SectionTypeId IS NULL
      AND prs.TypeKey IS NOT NULL
      AND LTRIM(RTRIM(prs.TypeKey)) <> '';
END
");

            // Any remaining NULL SectionTypeId rows => set to "Text"
            migrationBuilder.Sql(@"
DECLARE @TextId INT = (SELECT TOP 1 Id FROM dbo.SectionTypes WHERE [Name] = 'Text');
UPDATE dbo.PageSections SET SectionTypeId = @TextId WHERE SectionTypeId IS NULL;
UPDATE dbo.PageRevisionSections SET SectionTypeId = @TextId WHERE SectionTypeId IS NULL;
");

            // ------------------------------------------------------------
            // 6) Make SectionTypeId NOT NULL (after backfill!)
            // ------------------------------------------------------------
            migrationBuilder.AlterColumn<int>(
                name: "SectionTypeId",
                table: "PageSections",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SectionTypeId",
                table: "PageRevisionSections",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // ------------------------------------------------------------
            // 7) Indexes + FKs for SectionTypes
            // ------------------------------------------------------------
            migrationBuilder.CreateIndex(
                name: "IX_PageSections_SectionTypeId",
                table: "PageSections",
                column: "SectionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_SectionTypeId",
                table: "PageRevisionSections",
                column: "SectionTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_PageSections_SectionTypes_SectionTypeId",
                table: "PageSections",
                column: "SectionTypeId",
                principalTable: "SectionTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PageRevisionSections_SectionTypes_SectionTypeId",
                table: "PageRevisionSections",
                column: "SectionTypeId",
                principalTable: "SectionTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ------------------------------------------------------------
            // 8) Fix PageRevisionSections -> PageRevisions FK to be tenant-safe
            //    - Drop legacy FK/index (if they exist)
            //    - Create unique key on PageRevisions(TenantId, Id)
            //    - Create composite FK from PageRevisionSections(TenantId, PageRevisionId)
            // ------------------------------------------------------------
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PageRevisionSections_PageRevisions_PageRevisionId')
BEGIN
    ALTER TABLE dbo.PageRevisionSections DROP CONSTRAINT FK_PageRevisionSections_PageRevisions_PageRevisionId;
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PageRevisionSections_PageRevisionId' AND object_id = OBJECT_ID('dbo.PageRevisionSections'))
BEGIN
    DROP INDEX IX_PageRevisionSections_PageRevisionId ON dbo.PageRevisionSections;
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE name = 'AK_PageRevisions_TenantId_Id'
)
BEGIN
    ALTER TABLE dbo.PageRevisions
    ADD CONSTRAINT AK_PageRevisions_TenantId_Id UNIQUE (TenantId, Id);
END
");

            // Ensure uniqueness on revision sections per revision + sort order (tenant-safe)
            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_SortOrder",
                table: "PageRevisionSections",
                columns: new[] { "TenantId", "PageRevisionId", "SortOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PageRevisionSections_PageRevisions_TenantId_PageRevisionId",
                table: "PageRevisionSections",
                columns: new[] { "TenantId", "PageRevisionId" },
                principalTable: "PageRevisions",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            // ------------------------------------------------------------
            // 9) Drop legacy columns (now safe)
            // ------------------------------------------------------------
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageSections','TypeKey') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PageSections DROP COLUMN TypeKey;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageSections','ContentJson') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PageSections DROP COLUMN ContentJson;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageRevisionSections','TypeKey') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PageRevisionSections DROP COLUMN TypeKey;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageRevisionSections','ContentJson') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PageRevisionSections DROP COLUMN ContentJson;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NOTE: Down does not restore original legacy values (data-loss is expected on down).
            // It only restores schema shape.

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageSections','TypeKey') IS NULL
BEGIN
    ALTER TABLE dbo.PageSections ADD TypeKey nvarchar(100) NULL;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageSections','ContentJson') IS NULL
BEGIN
    ALTER TABLE dbo.PageSections ADD ContentJson nvarchar(max) NULL;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageRevisionSections','TypeKey') IS NULL
BEGIN
    ALTER TABLE dbo.PageRevisionSections ADD TypeKey nvarchar(100) NULL;
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.PageRevisionSections','ContentJson') IS NULL
BEGIN
    ALTER TABLE dbo.PageRevisionSections ADD ContentJson nvarchar(max) NULL;
END
");

            // Drop tenant-safe FK
            migrationBuilder.DropForeignKey(
                name: "FK_PageRevisionSections_PageRevisions_TenantId_PageRevisionId",
                table: "PageRevisionSections");

            migrationBuilder.DropIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_SortOrder",
                table: "PageRevisionSections");

            // Recreate legacy FK (single-column) if desired
            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_PageRevisionId",
                table: "PageRevisionSections",
                column: "PageRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PageRevisionSections_PageRevisions_PageRevisionId",
                table: "PageRevisionSections",
                column: "PageRevisionId",
                principalTable: "PageRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Drop SectionType FKs + columns + table
            migrationBuilder.DropForeignKey(
                name: "FK_PageRevisionSections_SectionTypes_SectionTypeId",
                table: "PageRevisionSections");

            migrationBuilder.DropForeignKey(
                name: "FK_PageSections_SectionTypes_SectionTypeId",
                table: "PageSections");

            migrationBuilder.DropIndex(
                name: "IX_PageRevisionSections_SectionTypeId",
                table: "PageRevisionSections");

            migrationBuilder.DropIndex(
                name: "IX_PageSections_SectionTypeId",
                table: "PageSections");

            migrationBuilder.DropColumn(
                name: "SectionTypeId",
                table: "PageRevisionSections");

            migrationBuilder.DropColumn(
                name: "SectionTypeId",
                table: "PageSections");

            migrationBuilder.DropTable(
                name: "SectionTypes");

            // Drop unique constraint if it exists
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE name = 'AK_PageRevisions_TenantId_Id'
)
BEGIN
    ALTER TABLE dbo.PageRevisions DROP CONSTRAINT AK_PageRevisions_TenantId_Id;
END
");
        }
    }
}
