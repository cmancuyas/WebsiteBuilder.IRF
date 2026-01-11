using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    public partial class UpdatePageRevisions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add PublishedRevisionId to Pages (safe)
            migrationBuilder.AddColumn<int>(
                name: "PublishedRevisionId",
                table: "Pages",
                type: "int",
                nullable: true);

            // 2) DROP + RECREATE revision tables to avoid int->uniqueidentifier conversion failure
            //    Order matters: drop child first.
            migrationBuilder.DropTable(
                name: "PageRevisionSections");

            migrationBuilder.DropTable(
                name: "PageRevisions");

            // 3) Recreate PageRevisions with TenantId = uniqueidentifier and BaseModel fields
            migrationBuilder.CreateTable(
                name: "PageRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),

                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),

                    // FK to Pages
                    PageId = table.Column<int>(type: "int", nullable: false),

                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    IsPublishedSnapshot = table.Column<bool>(type: "bit", nullable: false),

                    // Snapshot fields (adjust max lengths/types if your model differs)
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    LayoutKey = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MetaTitle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OgImageAssetId = table.Column<int>(type: "int", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),

                    // BaseModel fields
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),

                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValue: Guid.Empty),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),

                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),

                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),

                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageRevisions_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisions_PageId",
                table: "PageRevisions",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisions_TenantId_PageId_VersionNumber",
                table: "PageRevisions",
                columns: new[] { "TenantId", "PageId", "VersionNumber" },
                unique: true);

            // 4) Recreate PageRevisionSections with TenantId = uniqueidentifier and BaseModel fields
            migrationBuilder.CreateTable(
                name: "PageRevisionSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),

                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),

                    PageRevisionId = table.Column<int>(type: "int", nullable: false),

                    SourcePageSectionId = table.Column<int>(type: "int", nullable: true),

                    TypeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),

                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),

                    // BaseModel fields
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),

                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValue: Guid.Empty),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),

                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),

                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),

                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageRevisionSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageRevisionSections_PageRevisions_PageRevisionId",
                        column: x => x.PageRevisionId,
                        principalTable: "PageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_PageRevisionId",
                table: "PageRevisionSections",
                column: "PageRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId",
                table: "PageRevisionSections",
                columns: new[] { "TenantId", "PageRevisionId" });

            // 5) Add FK from Pages -> PageRevisions (PublishedRevisionId)
            migrationBuilder.CreateIndex(
                name: "IX_Pages_PublishedRevisionId",
                table: "Pages",
                column: "PublishedRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pages_PageRevisions_PublishedRevisionId",
                table: "Pages",
                column: "PublishedRevisionId",
                principalTable: "PageRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop FK and column on Pages
            migrationBuilder.DropForeignKey(
                name: "FK_Pages_PageRevisions_PublishedRevisionId",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_PublishedRevisionId",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "PublishedRevisionId",
                table: "Pages");

            // Drop revision tables (we don't try to revert to int TenantId; this is a dev-safe rollback)
            migrationBuilder.DropTable(name: "PageRevisionSections");
            migrationBuilder.DropTable(name: "PageRevisions");
        }
    }
}
