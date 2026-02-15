using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class BackfillOwnerUserIdForPagesAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Pages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "NavigationMenuItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "MediaAssets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Pages_TenantId_OwnerUserId",
                table: "Pages",
                columns: new[] { "TenantId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_TenantId_OwnerUserId",
                table: "MediaAssets",
                columns: new[] { "TenantId", "OwnerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pages_TenantId_OwnerUserId",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_TenantId_OwnerUserId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "MediaAssets");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "NavigationMenuItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);
        }
    }
}
