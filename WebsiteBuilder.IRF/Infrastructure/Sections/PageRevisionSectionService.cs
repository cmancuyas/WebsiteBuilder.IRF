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

        public async Task CompactSortOrderAsync(Guid tenantId, int pageRevisionId)
        {
            var sections = await _db.PageRevisionSections
                .Where(s =>
                    s.TenantId == tenantId &&
                    s.PageRevisionId == pageRevisionId &&
                    !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToListAsync();

            var expected = 1;
            var dirty = false;

            foreach (var s in sections)
            {
                if (s.SortOrder != expected)
                {
                    s.SortOrder = expected;
                    dirty = true;
                }
                expected++;
            }

            if (dirty)
                await _db.SaveChangesAsync();
        }
    }

}
