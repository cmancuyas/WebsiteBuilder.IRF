using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddTenantHomePage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<int>(
                name: "HomePageId",
                table: "Tenants",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_HomePageId",
                table: "Tenants",
                column: "HomePageId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Pages_HomePageId",
                table: "Tenants",
                column: "HomePageId",
                principalTable: "Pages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Pages_HomePageId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_HomePageId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "HomePageId",
                table: "Tenants");

        }
    }
}
