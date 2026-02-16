using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
// using WebsiteBuilder.IRF.Infrastructure.Pages;  // if you have a renderer service

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    [IgnoreAntiforgeryToken] // it's GET, but optional
    public class PreviewModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public PreviewModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public string PageTitle { get; private set; } = "";
        public string Slug { get; private set; } = "";
        public IReadOnlyList<PreviewSectionVm> Sections { get; private set; } = Array.Empty<PreviewSectionVm>();

        public sealed record PreviewSectionVm(
            int RevisionSectionId,
            string TypeKey,
            string SettingsJson,
            int SortOrder
        );

        public async Task<IActionResult> OnGetAsync(int id, string? rev = "draft", CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // ✅ Tenant + authorization: ensure this page belongs to this tenant (and user allowed)
            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            // Load draft revision
            if (page.DraftRevisionId == null)
                return Content("No draft exists for this page.", "text/plain");

            var draft = await _db.PageRevisions
                .AsNoTracking()
                .Include(r => r.Sections.Where(s => !s.IsDeleted && s.IsActive))
                    .ThenInclude(s => s.SectionType)
                .FirstOrDefaultAsync(r =>
                    r.Id == page.DraftRevisionId &&
                    r.TenantId == _tenant.TenantId &&
                    r.PageId == page.Id &&
                    !r.IsDeleted, ct);

            if (draft == null)
                return NotFound("Draft revision not found.");

            PageTitle = draft.Title ?? page.Title ?? "";
            Slug = draft.Slug ?? page.Slug ?? "";

            Sections = draft.Sections
                .OrderBy(s => s.SortOrder)
                .Select(s => new PreviewSectionVm(
                    s.Id,
                    s.SectionType.Key,
                    s.SettingsJson ?? "{}",
                    s.SortOrder
                ))
                .ToList();

            return Page();
        }
    }
}
