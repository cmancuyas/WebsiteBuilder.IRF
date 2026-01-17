using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class MigrateGalleryItemsToImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        UPDATE prs
        SET SettingsJson =
            JSON_MODIFY(
                JSON_MODIFY(prs.SettingsJson, '$.images', JSON_QUERY(prs.SettingsJson, '$.items')),
                '$.items', NULL
            )
        FROM PageRevisionSections prs
        INNER JOIN SectionTypes st ON st.Id = prs.SectionTypeId
        WHERE st.[Key] = 'gallery'
          AND prs.SettingsJson IS NOT NULL
          AND JSON_QUERY(prs.SettingsJson, '$.items') IS NOT NULL
          AND JSON_QUERY(prs.SettingsJson, '$.images') IS NULL;
    ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
