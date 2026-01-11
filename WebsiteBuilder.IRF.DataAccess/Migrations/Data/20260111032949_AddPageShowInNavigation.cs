using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddPageShowInNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NavigationOrderNavigationOrder",
                table: "Pages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ShowInNavigation",
                table: "Pages",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NavigationOrderNavigationOrder",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "ShowInNavigation",
                table: "Pages");
        }
    }
}
