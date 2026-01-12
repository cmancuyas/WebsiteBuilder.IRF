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

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        public string PageTitle { get; private set; } = string.Empty;
        public string PageSlug { get; private set; } = string.Empty;

        public List<PageSectionListItemVm> Sections { get; private set; } = new();

        // Add form (canonical)
        [BindProperty]
        public int NewSectionTypeId { get; set; }

        // Edit form (canonical)
        [BindProperty]
        public PageSectionEditVm Edit { get; set; } = new();

        // Dropdown options from DB SectionTypes (Id/Name)
        public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!_tenant.IsResolved) return NotFound();

            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == Id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted);

            if (page is null) return NotFound();

            PageTitle = page.Title;
            PageSlug = page.Slug;

            var sectionTypes = await _db.SectionTypes
                .AsNoTracking()
                .OrderBy(st => st.Name)
                .ToListAsync();

            SectionTypeOptions = sectionTypes
                .Select(st => new SelectListItem { Value = st.Id.ToString(), Text = st.Name })
                .ToList();

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
                    SectionTypeName = s.SectionType != null ? s.SectionType.Name : $"Type #{s.SectionTypeId}",
                    SettingsJson = s.SettingsJson
                })
                .ToListAsync();

            return Page();
        }

        // POST: Add section
        public async Task<IActionResult> OnPostAddAsync()
        {
            if (!_tenant.IsResolved) return NotFound();
            if (Id <= 0) return NotFound();

            var pageExists = await _db.Pages.AnyAsync(p =>
                p.Id == Id &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted);

            if (!pageExists) return NotFound();

            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == NewSectionTypeId);

            if (sectionType == null)
            {
                TempData["Err"] = "Invalid section type.";
                return RedirectToPage("./Sections", new { id = Id });
            }

            var defaultJson = "{}";
            if (_sections.TryGet(sectionType.Name, out var def))
                defaultJson = def.DefaultJson ?? "{}";

            var maxOrder = await _db.PageSections
                .Where(s => s.PageId == Id && s.TenantId == _tenant.TenantId && !s.IsDeleted)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync() ?? 0;

            var entity = new PageSection
            {
                TenantId = _tenant.TenantId,
                PageId = Id,
                SortOrder = (maxOrder <= 0 ? 0 : maxOrder) + 10,

                SectionTypeId = sectionType.Id,
                SettingsJson = defaultJson,

                IsDeleted = false,
                CreatedBy = GetUserIdOrEmpty(),
                CreatedAt = DateTime.UtcNow
            };

            _db.PageSections.Add(entity);
            await _db.SaveChangesAsync();

            TempData["Ok"] = "Section added.";
            return RedirectToPage("./Sections", new { id = Id });
        }

        // POST: Update section
        public async Task<IActionResult> OnPostUpdateAsync()
        {
            if (!_tenant.IsResolved) return NotFound();
            if (Id <= 0) return NotFound();

            if (!ModelState.IsValid)
            {
                TempData["Err"] = "Invalid section data.";
                return RedirectToPage("./Sections", new { id = Id });
            }

            var entity = await _db.PageSections.FirstOrDefaultAsync(s =>
                s.Id == Edit.Id &&
                s.PageId == Id &&
                s.TenantId == _tenant.TenantId &&
                !s.IsDeleted);

            if (entity is null) return NotFound();

            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == Edit.SectionTypeId);

            if (sectionType == null)
            {
                TempData["Err"] = "Invalid section type.";
                return RedirectToPage("./Sections", new { id = Id });
            }

            var json = (Edit.SettingsJson ?? "{}").Trim();

            if (!_validator.Validate(sectionType.Name, json, out var error))
            {
                TempData["Err"] = error;
                return RedirectToPage("./Sections", new { id = Id });
            }

            entity.SectionTypeId = sectionType.Id;
            entity.SettingsJson = json;

            entity.UpdatedBy = GetUserIdOrEmpty();
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Ok"] = "Section updated.";
            return RedirectToPage("./Sections", new { id = Id });
        }

        // POST: Delete section (soft)
        public async Task<IActionResult> OnPostDeleteAsync(int sectionId)
        {
            if (!_tenant.IsResolved) return NotFound();
            if (Id <= 0) return NotFound();

            var entity = await _db.PageSections.FirstOrDefaultAsync(s =>
                s.Id == sectionId &&
                s.PageId == Id &&
                s.TenantId == _tenant.TenantId &&
                !s.IsDeleted);

            if (entity is null) return NotFound();

            entity.IsDeleted = true;
            entity.UpdatedBy = GetUserIdOrEmpty();
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Ok"] = "Section deleted.";
            return RedirectToPage("./Sections", new { id = Id });
        }

        // POST: Reorder (AJAX)
        public async Task<IActionResult> OnPostReorderAsync([FromBody] PageSectionReorderVm vm)
        {
            if (!_tenant.IsResolved) return NotFound();
            if (Id <= 0) return NotFound();

            if (vm?.OrderedIds == null || vm.OrderedIds.Count == 0)
                return BadRequest(new { ok = false, message = "No IDs provided." });

            var sections = await _db.PageSections
                .Where(s => s.PageId == Id && s.TenantId == _tenant.TenantId && !s.IsDeleted)
                .ToListAsync();

            if (sections.Count == 0)
                return BadRequest(new { ok = false, message = "No sections to reorder." });

            var currentIds = sections.Select(s => s.Id).ToHashSet();
            foreach (var sid in vm.OrderedIds)
                if (!currentIds.Contains(sid))
                    return BadRequest(new { ok = false, message = "Invalid section id in reorder list." });

            if (vm.OrderedIds.Count != sections.Count)
                return BadRequest(new { ok = false, message = "Reorder list does not match section count." });

            var now = DateTime.UtcNow;
            var userId = GetUserIdOrEmpty();

            var byId = sections.ToDictionary(s => s.Id);
            var order = 10;

            foreach (var sid in vm.OrderedIds)
            {
                var s = byId[sid];
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
