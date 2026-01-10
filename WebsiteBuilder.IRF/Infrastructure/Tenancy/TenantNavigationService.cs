using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed record NavItem(string Title, string Url);

    public sealed class TenantNavigationService : ITenantNavigationService
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public TenantNavigationService(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public async Task<IReadOnlyList<NavItem>> GetNavAsync(CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return Array.Empty<NavItem>();

            var pages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published)
                .OrderBy(p => p.Title)
                .Select(p => new NavItem(
                    p.Title,
                    "/" + (p.Slug ?? "").Trim().TrimStart('/')))
                .ToListAsync(ct);

            return pages;
        }
    }
}
