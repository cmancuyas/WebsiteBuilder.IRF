using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public class SectionsModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ISectionRegistry _sections;
        private readonly ISectionJsonValidator _validator;

        public SectionsModel(
            DataContext db,
            ITenantContext tenant,
            ISectionRegistry sections,
            ISectionJsonValidator validator)
        {
            _db = db;
            _tenant = tenant;
            _sections = sections;
            _validator = validator;
        }

        // GET only (safe)
        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        public string PageTitle { get; private set; } = string.Empty;
        public string PageSlug { get; private set; } = string.Empty;

        public List<PageSectionListItemVm> Sections { get; private set; } = new();

        [BindProperty]
        public int NewSectionTypeId { get; set; }

        [BindProperty]
        public PageSectionEditVm Edit { get; set; } = new();

        public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

        // ===================== GET =====================
        public async Task<IActionResult> OnGetAsync()
        {
            if (!_tenant.IsResolved) return NotFound();

            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == Id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted);

            if (page == null) return NotFound();

            PageTitle = page.Title;
            PageSlug = page.Slug;

            SectionTypeOptions = await _db.SectionTypes
                .AsNoTracking()
                .OrderBy(st => st.Name)
                .Select(st => new SelectListItem
                {
                    Value = st.Id.ToString(),
                    Text = st.Name
                })
                .ToListAsync();

            Sections = await _db.PageSections
                .AsNoTracking()
                .Include(s => s.SectionType)
                .Where(s =>
                    s.PageId == Id &&
                    s.TenantId == _tenant.TenantId &&
                    !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .Select(s => new PageSectionListItemVm
                {
                    Id = s.Id,
                    SortOrder = s.SortOrder,
                    SectionTypeId = s.SectionTypeId,
                    SectionTypeName = s.SectionType != null
                        ? s.SectionType.Name
                        : $"Type #{s.SectionTypeId}",
                    SettingsJson = s.SettingsJson
                })
                .ToListAsync();

            return Page();
        }

        // ===================== ADD =====================
        public async Task<IActionResult> OnPostAddAsync(int pageId)
        {
            if (!_tenant.IsResolved) return NotFound();
            if (pageId <= 0) return NotFound();

            var pageExists = await _db.Pages.AnyAsync(p =>
                p.Id == pageId &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted);

            if (!pageExists) return NotFound();

            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == NewSectionTypeId);

            if (sectionType == null)
            {
                TempData["Err"] = "Invalid section type.";
                return RedirectToPage("./Sections", new { id = pageId });
            }

            var defaultJson = "{}";
            if (_sections.TryGet(sectionType.Name, out var def))
                defaultJson = def.DefaultJson ?? "{}";

            var maxOrder = await _db.PageSections
                .Where(s => s.PageId == pageId && s.TenantId == _tenant.TenantId && !s.IsDeleted)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync() ?? 0;

            var entity = new PageSection
            {
                TenantId = _tenant.TenantId,
                PageId = pageId,
                SortOrder = maxOrder + 10,
                SectionTypeId = sectionType.Id,
                SettingsJson = defaultJson,
                CreatedBy = GetUserIdOrEmpty(),
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _db.PageSections.Add(entity);
            await _db.SaveChangesAsync();

            return RedirectToPage("./Sections", new { id = pageId });
        }

        // ===================== UPDATE =====================
        public async Task<IActionResult> OnPostUpdateAsync(int pageId)
        {
            if (!_tenant.IsResolved) return NotFound();
            if (pageId <= 0) return NotFound();

            var entity = await _db.PageSections.FirstOrDefaultAsync(s =>
                s.Id == Edit.Id &&
                s.PageId == pageId &&
                s.TenantId == _tenant.TenantId &&
                !s.IsDeleted);

            if (entity == null) return NotFound();

            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == Edit.SectionTypeId);

            if (sectionType == null)
                return RedirectToPage("./Sections", new { id = pageId });

            var json = (Edit.SettingsJson ?? "{}").Trim();
            if (!_validator.Validate(sectionType.Name, json, out var error))
            {
                TempData["Err"] = error;
                return RedirectToPage("./Sections", new { id = pageId });
            }

            entity.SectionTypeId = sectionType.Id;
            entity.SettingsJson = json;
            entity.UpdatedBy = GetUserIdOrEmpty();
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return RedirectToPage("./Sections", new { id = pageId });
        }

        // ===================== DELETE =====================
        public async Task<IActionResult> OnPostDeleteAsync(int pageId, int sectionId)
        {
            if (!_tenant.IsResolved) return NotFound();
            if (pageId <= 0) return NotFound();

            var entity = await _db.PageSections.FirstOrDefaultAsync(s =>
                s.Id == sectionId &&
                s.PageId == pageId &&
                s.TenantId == _tenant.TenantId &&
                !s.IsDeleted);

            if (entity == null) return NotFound();

            entity.IsDeleted = true;
            entity.UpdatedBy = GetUserIdOrEmpty();
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return RedirectToPage("./Sections", new { id = pageId });
        }

        // ===================== REORDER (AJAX) =====================
        public async Task<IActionResult> OnPostReorderAsync(int pageId, [FromBody] PageSectionReorderVm vm)
        {
            if (!_tenant.IsResolved) return NotFound();
            if (pageId <= 0) return NotFound();

            if (vm?.OrderedIds == null || vm.OrderedIds.Count == 0)
                return BadRequest(new { ok = false });

            var sections = await _db.PageSections
                .Where(s => s.PageId == pageId && s.TenantId == _tenant.TenantId && !s.IsDeleted)
                .ToListAsync();

            var byId = sections.ToDictionary(s => s.Id);
            var order = 10;
            var now = DateTime.UtcNow;
            var userId = GetUserIdOrEmpty();

            foreach (var sid in vm.OrderedIds)
            {
                if (!byId.TryGetValue(sid, out var s)) continue;
                s.SortOrder = order;
                s.UpdatedAt = now;
                s.UpdatedBy = userId;
                order += 10;
            }

            await _db.SaveChangesAsync();
            return new JsonResult(new { ok = true });
        }

        private Guid GetUserIdOrEmpty()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }
}
