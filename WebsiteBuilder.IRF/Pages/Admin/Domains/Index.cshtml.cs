using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Domains;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Domains
{
    public sealed class IndexModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IDomainVerificationService _verifier;
        private readonly IOutputCacheStore _outputCache;

        public IndexModel(DataContext db, ITenantContext tenant, IDomainVerificationService verifier, IOutputCacheStore outputCache)
        {
            _db = db;
            _tenant = tenant;
            _verifier = verifier;
            _outputCache = outputCache;
        }

        public string? FlashMessage { get; private set; }

        public List<DomainRow> Domains { get; private set; } = new();

        public sealed record DomainRow(
            int Id,
            string Host,
            string NormalizedHost,
            bool IsPrimary,
            int VerificationStatusId,
            string VerificationToken,
            DateTime? VerifiedAt,
            DateTime? ActivatedAt,
            string? LastVerificationError
        );

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            var tenantId = GetTenantIdOrThrow();

            Domains = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId && !d.IsDeleted)
                .OrderByDescending(d => d.IsPrimary)
                .ThenBy(d => d.Host)
                .Select(d => new DomainRow(
                    d.Id,
                    d.Host,
                    d.NormalizedHost,
                    d.IsPrimary,
                    d.VerificationStatusId,
                    d.VerificationToken,
                    d.VerifiedAt,
                    d.ActivatedAt,
                    d.LastVerificationError
                ))
                .ToListAsync(ct);

            if (TempData.TryGetValue("Flash", out var flashObj))
                FlashMessage = flashObj?.ToString();

            return Page();
        }

        public string GetDnsHost(string normalizedHost) => $"_wb-verify.{normalizedHost}";
        public string GetDnsValue(string token) => $"wb={token}";

        public async Task<IActionResult> OnPostVerifyAsync(int id, CancellationToken ct)
        {
            var tenantId = GetTenantIdOrThrow();

            var dm = await _db.DomainMappings
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, ct);

            if (dm is null) return NotFound();

            dm.LastVerificationCheckAt = DateTime.UtcNow;

            var (ok, error) = await _verifier.VerifyDnsTxtAsync(dm.NormalizedHost, dm.VerificationToken, ct);

            if (ok)
            {
                dm.VerificationStatusId = DomainVerificationStatus.Verified;
                dm.VerifiedAt = DateTime.UtcNow;
                dm.LastVerificationError = null;
            }
            else
            {
                dm.VerificationStatusId = DomainVerificationStatus.Failed;
                dm.LastVerificationError = error;
            }

            await _db.SaveChangesAsync(ct);
            TempData["Flash"] = ok ? "Domain verified successfully." : "Verification failed. Check TXT record and try again.";

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostActivateAsync(int id, CancellationToken ct)
        {
            var tenantId = GetTenantIdOrThrow();

            var dm = await _db.DomainMappings
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, ct);

            if (dm is null) return NotFound();

            if (dm.VerificationStatusId != DomainVerificationStatus.Verified)
            {
                TempData["Flash"] = "Cannot activate: domain is not verified.";
                return RedirectToPage();
            }

            if (dm.ActivatedAt is null)
            {
                dm.IsActive = true;
                dm.ActivatedAt = DateTime.UtcNow;
                dm.DeactivatedAt = null;
                await _db.SaveChangesAsync(ct);
                await EvictTenantPublicCachesAsync(tenantId, ct);
            }

            TempData["Flash"] = "Domain activated.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeactivateAsync(int id, CancellationToken ct)
        {
            var tenantId = GetTenantIdOrThrow();

            var dm = await _db.DomainMappings
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, ct);

            if (dm is null) return NotFound();

            if (dm.ActivatedAt is not null)
            {
                dm.IsActive = false;
                dm.DeactivatedAt = DateTime.UtcNow;
                dm.ActivatedAt = null;

                // If you deactivate the primary, also unset primary
                dm.IsPrimary = false;

                await _db.SaveChangesAsync(ct);
                await EvictTenantPublicCachesAsync(tenantId, ct);
            }

            TempData["Flash"] = "Domain deactivated.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSetPrimaryAsync(int id, CancellationToken ct)
        {
            var tenantId = GetTenantIdOrThrow();

            var dm = await _db.DomainMappings
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, ct);

            if (dm is null) return NotFound();

            if (dm.VerificationStatusId != DomainVerificationStatus.Verified || dm.ActivatedAt is null || !dm.IsActive)
            {
                TempData["Flash"] = "Cannot set primary: domain must be verified and activated.";
                return RedirectToPage();
            }

            // Transaction: unset old primary, set new
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var currentPrimary = await _db.DomainMappings
                .Where(x => x.TenantId == tenantId && x.IsPrimary && !x.IsDeleted)
                .ToListAsync(ct);

            foreach (var p in currentPrimary)
                p.IsPrimary = false;

            dm.IsPrimary = true;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await EvictTenantPublicCachesAsync(tenantId, ct);

            TempData["Flash"] = "Primary domain updated.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
        {
            var tenantId = GetTenantIdOrThrow();

            var dm = await _db.DomainMappings
                .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, ct);

            if (dm is null) return NotFound();

            dm.IsDeleted = true;
            dm.IsActive = false;
            dm.IsPrimary = false;
            dm.DeactivatedAt = DateTime.UtcNow;
            dm.ActivatedAt = null;

            await _db.SaveChangesAsync(ct);
            await EvictTenantPublicCachesAsync(tenantId, ct);

            TempData["Flash"] = "Domain mapping deleted (soft delete).";
            return RedirectToPage();
        }

        private Guid GetTenantIdOrThrow()
        {
            if (_tenant.IsResolved && _tenant.TenantId != Guid.Empty)
                return _tenant.TenantId;

            if (HttpContext.Items.TryGetValue("TenantId", out var obj) && obj is Guid gid && gid != Guid.Empty)
                return gid;

            throw new InvalidOperationException("Tenant not resolved in admin context.");
        }
        private async Task EvictTenantPublicCachesAsync(Guid tenantId, CancellationToken ct)
        {
            await _outputCache.EvictByTagAsync($"tenant:{tenantId}", ct);
            await _outputCache.EvictByTagAsync("public-pages", ct);
        }
    }
}