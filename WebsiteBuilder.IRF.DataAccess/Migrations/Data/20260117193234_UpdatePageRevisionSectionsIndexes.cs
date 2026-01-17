using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class UpdatePageRevisionSectionsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[PageSection]', N'U') IS NOT NULL
    DROP TABLE [dbo].[PageSection];
");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Pages_TenantId_Id",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_SortOrder",
                table: "PageRevisionSections");

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_IsDeleted_IsActive",
                table: "PageRevisionSections",
                columns: new[] { "TenantId", "PageRevisionId", "IsDeleted", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_SortOrder",
                table: "PageRevisionSections",
                columns: new[] { "TenantId", "PageRevisionId", "SortOrder" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_IsDeleted_IsActive",
                table: "PageRevisionSections");

            migrationBuilder.DropIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_SortOrder",
                table: "PageRevisionSections");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Pages_TenantId_Id",
                table: "Pages",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "PageSection",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    SectionTypeId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageSection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageSection_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PageSection_SectionTypes_SectionTypeId",
                        column: x => x.SectionTypeId,
                        principalTable: "SectionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageRevisionSections_TenantId_PageRevisionId_SortOrder",
                table: "PageRevisionSections",
                columns: new[] { "TenantId", "PageRevisionId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageSection_PageId",
                table: "PageSection",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_PageSection_SectionTypeId",
                table: "PageSection",
                column: "SectionTypeId");
        }
    }
}
