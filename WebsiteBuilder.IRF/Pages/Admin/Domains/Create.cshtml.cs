using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Domains
{
    public sealed class CreateModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public CreateModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public DomainMapping? Created { get; private set; }
        public string DnsHost { get; private set; } = "";
        public string DnsValue { get; private set; } = "";

        public sealed class InputModel
        {
            [Required, MaxLength(510)]
            public string Host { get; set; } = "";

            public int SslModeId { get; set; } = DomainSslMode.External;
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            var tenantId = GetTenantIdOrThrow();

            if (!ModelState.IsValid)
                return Page();

            var hostRaw = Input.Host ?? "";
            var normalized = HostNormalizer.Normalize(hostRaw);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                ModelState.AddModelError(nameof(Input.Host), "Invalid host.");
                return Page();
            }

            // Prevent collisions across tenants (anti-hijack)
            var exists = await _db.DomainMappings
                .AsNoTracking()
                .AnyAsync(d => d.NormalizedHost == normalized && !d.IsDeleted, ct);

            if (exists)
            {
                ModelState.AddModelError(nameof(Input.Host), "This domain is already mapped to a tenant.");
                return Page();
            }

            var token = Base64UrlToken(32);

            var dm = new DomainMapping
            {
                TenantId = tenantId,
                Host = hostRaw.Trim(),
                NormalizedHost = normalized,

                IsPrimary = false,

                VerificationStatusId = DomainVerificationStatus.Pending,
                VerificationMethodId = DomainVerificationMethod.DnsTxt,

                SslModeId = Input.SslModeId,

                VerificationToken = token,
                VerificationTokenCreatedAt = DateTime.UtcNow,

                // Start inactive until verified+activated
                IsActive = false,
                ActivatedAt = null
            };

            _db.DomainMappings.Add(dm);
            await _db.SaveChangesAsync(ct);

            Created = dm;
            DnsHost = $"_wb-verify.{dm.NormalizedHost}";
            DnsValue = $"wb={dm.VerificationToken}";

            return Page();
        }

        private Guid GetTenantIdOrThrow()
        {
            if (_tenant.IsResolved && _tenant.TenantId != Guid.Empty)
                return _tenant.TenantId;

            if (HttpContext.Items.TryGetValue("TenantId", out var obj) && obj is Guid gid && gid != Guid.Empty)
                return gid;

            throw new InvalidOperationException("Tenant not resolved in admin context.");
        }

        private static string Base64UrlToken(int bytes)
        {
            var data = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}