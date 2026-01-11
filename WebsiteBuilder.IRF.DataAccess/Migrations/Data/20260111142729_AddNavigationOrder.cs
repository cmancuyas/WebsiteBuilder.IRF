using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddNavigationOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NavigationOrderNavigationOrder",
                table: "Pages",
                newName: "NavigationOrder");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "PageStatuses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "PageStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "SortOrder",
                value: 0);

            migrationBuilder.UpdateData(
                table: "PageStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "SortOrder",
                value: 0);

            migrationBuilder.UpdateData(
                table: "PageStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "SortOrder",
                value: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "PageStatuses");

            migrationBuilder.RenameColumn(
                name: "NavigationOrder",
                table: "Pages",
                newName: "NavigationOrderNavigationOrder");
        }
    }
}
