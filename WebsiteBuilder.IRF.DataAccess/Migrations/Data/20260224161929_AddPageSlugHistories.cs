using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddPageSlugHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PageSlugHistories_TenantId_IsDeleted_IsActive",
                table: "PageSlugHistories",
                columns: new[] { "TenantId", "IsDeleted", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PageSlugHistories_TenantId_OldSlug",
                table: "PageSlugHistories",
                columns: new[] { "TenantId", "OldSlug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageSlugHistories_TenantId_PageId",
                table: "PageSlugHistories",
                columns: new[] { "TenantId", "PageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PageSlugHistories_TenantId_IsDeleted_IsActive",
                table: "PageSlugHistories");

            migrationBuilder.DropIndex(
                name: "IX_PageSlugHistories_TenantId_OldSlug",
                table: "PageSlugHistories");

            migrationBuilder.DropIndex(
                name: "IX_PageSlugHistories_TenantId_PageId",
                table: "PageSlugHistories");
        }
    }
}
