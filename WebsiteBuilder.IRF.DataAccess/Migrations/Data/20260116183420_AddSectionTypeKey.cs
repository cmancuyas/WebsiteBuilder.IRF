using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddSectionTypeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Fill empty/null Key from Name
            migrationBuilder.Sql(@"
UPDATE st
SET [Key] = [Name]
FROM [SectionTypes] st
WHERE (st.[Key] IS NULL OR LTRIM(RTRIM(st.[Key])) = '')
  AND st.[Name] IS NOT NULL AND LTRIM(RTRIM(st.[Name])) <> '';
");

            // 2) Normalize: trim + lowercase
            migrationBuilder.Sql(@"
UPDATE st
SET [Key] = LOWER(LTRIM(RTRIM(st.[Key])))
FROM [SectionTypes] st
WHERE st.[Key] IS NOT NULL AND LTRIM(RTRIM(st.[Key])) <> '';
");

            // 3) Normalize: spaces/underscores -> hyphens
            migrationBuilder.Sql(@"
UPDATE st
SET [Key] = REPLACE(REPLACE(st.[Key], ' ', '-'), '_', '-')
FROM [SectionTypes] st
WHERE st.[Key] IS NOT NULL AND LTRIM(RTRIM(st.[Key])) <> '';
");

            // 4) Resolve duplicates by suffixing Id (guarantees uniqueness)
            migrationBuilder.Sql(@"
;WITH d AS (
    SELECT [Key]
    FROM [SectionTypes]
    WHERE [Key] IS NOT NULL
    GROUP BY [Key]
    HAVING COUNT(*) > 1
)
UPDATE st
SET [Key] = CONCAT(st.[Key], '-', st.[Id])
FROM [SectionTypes] st
INNER JOIN d ON d.[Key] = st.[Key];
");

            // 5) Final safety: if Key is still empty/null, force a deterministic key
            migrationBuilder.Sql(@"
UPDATE st
SET [Key] = CONCAT('sectiontype-', st.[Id])
FROM [SectionTypes] st
WHERE st.[Key] IS NULL OR LTRIM(RTRIM(st.[Key])) = '';
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No safe down operation here because it would destroy data intent.
            // Leave empty intentionally.
        }
    }
}

