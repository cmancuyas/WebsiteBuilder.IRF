using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public sealed class PageRevisionSectionService : IPageRevisionSectionService
    {
        private readonly DataContext _db;

        public PageRevisionSectionService(DataContext db)
        {
            _db = db;
        }

        public async Task CompactSortOrderAsync(Guid tenantId, int pageRevisionId, CancellationToken ct = default)
        {
            var sections = await _db.PageRevisionSections
                .AsTracking()
                .Where(s =>
                    s.TenantId == tenantId &&
                    s.PageRevisionId == pageRevisionId &&
                    !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToListAsync(ct);

            var next = 1;
            foreach (var s in sections)
            {
                if (s.SortOrder != next)
                    s.SortOrder = next;

                next++;
            }

            await _db.SaveChangesAsync(ct);
        }

    }

}
