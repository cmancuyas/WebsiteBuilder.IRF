using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddPageStatusIsSystemAndUniqueName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PageSections_Pages_PageId",
                table: "PageSections");

            migrationBuilder.DropIndex(
                name: "IX_PageSections_PageId_SortOrder",
                table: "PageSections");

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "PageStatuses",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Pages_TenantId_Id",
                table: "Pages",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.UpdateData(
                table: "PageStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsSystem",
                value: true);

            migrationBuilder.UpdateData(
                table: "PageStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsSystem",
                value: true);

            migrationBuilder.UpdateData(
                table: "PageStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "IsSystem",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageStatuses_Name",
                table: "PageStatuses",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageSections_TenantId_PageId_SortOrder",
                table: "PageSections",
                columns: new[] { "TenantId", "PageId", "SortOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PageSections_Pages_TenantId_PageId",
                table: "PageSections",
                columns: new[] { "TenantId", "PageId" },
                principalTable: "Pages",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PageSections_Pages_TenantId_PageId",
                table: "PageSections");

            migrationBuilder.DropIndex(
                name: "IX_PageStatuses_Name",
                table: "PageStatuses");

            migrationBuilder.DropIndex(
                name: "IX_PageSections_TenantId_PageId_SortOrder",
                table: "PageSections");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Pages_TenantId_Id",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "PageStatuses");

            migrationBuilder.CreateIndex(
                name: "IX_PageSections_PageId_SortOrder",
                table: "PageSections",
                columns: new[] { "PageId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_PageSections_Pages_PageId",
                table: "PageSections",
                column: "PageId",
                principalTable: "Pages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
