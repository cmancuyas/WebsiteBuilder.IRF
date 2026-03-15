using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.IRF.DataAccess.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddDomainOnboardingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop old indexes (EF may have created these as unique previously)
            migrationBuilder.DropIndex(
                name: "IX_DomainMappings_Host",
                table: "DomainMappings");

            migrationBuilder.DropIndex(
                name: "IX_DomainMappings_TenantId_IsPrimary",
                table: "DomainMappings");

            // New onboarding fields
            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "DomainMappings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerificationCheckAt",
                table: "DomainMappings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastVerificationError",
                table: "DomainMappings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedHost",
                table: "DomainMappings",
                type: "nvarchar(510)",
                maxLength: 510,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VerificationToken",
                table: "DomainMappings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationTokenCreatedAt",
                table: "DomainMappings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "DomainMappings",
                type: "datetime2",
                nullable: true);

            // ------------------------------------------------------------------
            // Backfill + normalize NormalizedHost for existing rows
            // (SQL best-effort; app uses HostNormalizer with IDN as well)
            // ------------------------------------------------------------------
            migrationBuilder.Sql(@"
                UPDATE DomainMappings
                SET NormalizedHost = LOWER(RTRIM(LTRIM(Host)))
                WHERE (NormalizedHost = '' OR NormalizedHost IS NULL) AND Host IS NOT NULL;
            ");

            // Strip trailing dot
            migrationBuilder.Sql(@"
                UPDATE DomainMappings
                SET NormalizedHost = LEFT(NormalizedHost, LEN(NormalizedHost) - 1)
                WHERE NormalizedHost LIKE '%.';
            ");

            // Strip port if present (rare, but safe)
            migrationBuilder.Sql(@"
                UPDATE DomainMappings
                SET NormalizedHost = LEFT(NormalizedHost, CHARINDEX(':', NormalizedHost) - 1)
                WHERE CHARINDEX(':', NormalizedHost) > 0;
            ");

            // ------------------------------------------------------------------
            // Dedupe collisions before adding unique index on NormalizedHost
            // Keep: IsPrimary desc, then Id asc; soft-delete the rest.
            // ------------------------------------------------------------------
            migrationBuilder.Sql(@"
                ;WITH cte AS (
                    SELECT
                        Id,
                        NormalizedHost,
                        ROW_NUMBER() OVER (
                            PARTITION BY NormalizedHost
                            ORDER BY
                                CASE WHEN IsPrimary = 1 THEN 0 ELSE 1 END,
                                Id ASC
                        ) AS rn
                    FROM DomainMappings
                    WHERE IsDeleted = 0 AND NormalizedHost IS NOT NULL AND NormalizedHost <> ''
                )
                UPDATE dm
                SET IsDeleted = 1,
                    IsActive = 0,
                    IsPrimary = 0
                FROM DomainMappings dm
                INNER JOIN cte ON cte.Id = dm.Id
                WHERE cte.rn > 1;
            ");

            // ------------------------------------------------------------------
            // Recreate indexes per current model configuration
            // ------------------------------------------------------------------

            // Non-unique helper index for Host
            migrationBuilder.CreateIndex(
                name: "IX_DomainMappings_Host",
                table: "DomainMappings",
                column: "Host");

            // ✅ Global uniqueness on NormalizedHost
            migrationBuilder.CreateIndex(
                name: "IX_DomainMappings_NormalizedHost",
                table: "DomainMappings",
                column: "NormalizedHost",
                unique: true);

            // ✅ One primary domain per tenant (ignore soft deleted)
            migrationBuilder.CreateIndex(
                name: "IX_DomainMappings_TenantId_IsPrimary",
                table: "DomainMappings",
                columns: new[] { "TenantId", "IsPrimary" },
                unique: true,
                filter: "[IsPrimary] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop new indexes
            migrationBuilder.DropIndex(
                name: "IX_DomainMappings_Host",
                table: "DomainMappings");

            migrationBuilder.DropIndex(
                name: "IX_DomainMappings_NormalizedHost",
                table: "DomainMappings");

            migrationBuilder.DropIndex(
                name: "IX_DomainMappings_TenantId_IsPrimary",
                table: "DomainMappings");

            // Drop new columns
            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "DomainMappings");

            migrationBuilder.DropColumn(
                name: "LastVerificationCheckAt",
                table: "DomainMappings");

            migrationBuilder.DropColumn(
                name: "LastVerificationError",
                table: "DomainMappings");

            migrationBuilder.DropColumn(
                name: "NormalizedHost",
                table: "DomainMappings");

            migrationBuilder.DropColumn(
                name: "VerificationToken",
                table: "DomainMappings");

            migrationBuilder.DropColumn(
                name: "VerificationTokenCreatedAt",
                table: "DomainMappings");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "DomainMappings");

            // Restore previous indexes (Host unique + primary unique without IsDeleted filter)
            // NOTE: If your old schema was different, adjust accordingly.
            migrationBuilder.CreateIndex(
                name: "IX_DomainMappings_Host",
                table: "DomainMappings",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DomainMappings_TenantId_IsPrimary",
                table: "DomainMappings",
                columns: new[] { "TenantId", "IsPrimary" },
                unique: true,
                filter: "[IsPrimary] = 1");
        }
    }
}