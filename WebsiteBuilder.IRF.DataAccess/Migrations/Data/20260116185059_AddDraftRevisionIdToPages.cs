using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddDraftRevisionIdToPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DraftRevisionId",
                table: "Pages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_DraftRevisionId",
                table: "Pages",
                column: "DraftRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pages_PageRevisions_DraftRevisionId",
                table: "Pages",
                column: "DraftRevisionId",
                principalTable: "PageRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pages_PageRevisions_DraftRevisionId",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Pages_DraftRevisionId",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "DraftRevisionId",
                table: "Pages");
        }
    }
}
