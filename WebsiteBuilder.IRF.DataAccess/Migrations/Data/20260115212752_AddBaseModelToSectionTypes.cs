using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddBaseModelToSectionTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "SectionTypes",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "SectionTypes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "SectionTypes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "SectionTypes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "SectionTypes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "SectionTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "SectionTypes",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SectionTypes",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SectionTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SectionTypes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "SectionTypes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SectionTypes_IsActive_IsDeleted",
                table: "SectionTypes",
                columns: new[] { "IsActive", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SectionTypes_IsActive_IsDeleted",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SectionTypes");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "SectionTypes");
        }
    }
}
