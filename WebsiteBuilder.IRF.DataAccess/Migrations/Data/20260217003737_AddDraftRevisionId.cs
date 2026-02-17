using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddDraftRevisionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Pages_PageStatusId",
                table: "Pages",
                column: "PageStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pages_PageStatuses_PageStatusId",
                table: "Pages",
                column: "PageStatusId",
                principalTable: "PageStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pages_PageStatuses_PageStatusId",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_PageStatusId",
                table: "Pages");
        }
    }
}
