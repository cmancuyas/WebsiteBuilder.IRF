using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public class SectionsModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public SectionsModel(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        // Route parameter: /admin/pages/edit/{id:int}/sections
        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        public string PageTitle { get; private set; } = string.Empty;
        public string PageSlug { get; private set; } = string.Empty;

        public List<PageSectionListItemVm> Sections { get; private set; } = new();

        // Add form
        [BindProperty]
        public string NewTypeKey { get; set; } = "text";

        // Update form
        [BindProperty]
        public PageSectionEditVm Edit { get; set; } = new();

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

            Sections = await _db.PageSections
                .AsNoTracking()
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
                    TypeKey = s.TypeKey,
                    ContentJson = s.ContentJson
                })
                .ToListAsync();

            return Page();
        }

        // POST: Add a new section at bottom
        public async Task<IActionResult> OnPostAddAsync()
        {
            if (!_tenant.IsResolved) return NotFound();

            if (Id <= 0) return NotFound();

            if (string.IsNullOrWhiteSpace(NewTypeKey))
            {
                TempData["Err"] = "TypeKey is required.";
                return RedirectToPage("./Sections", new { id = Id });
            }

            var typeKey = NormalizeTypeKey(NewTypeKey);

            var pageExists = await _db.Pages.AnyAsync(p =>
                p.Id == Id &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted);

            if (!pageExists) return NotFound();

            // Use increments of 10 to allow easy inserts later
            var maxOrder = await _db.PageSections
                .Where(s => s.PageId == Id && s.TenantId == _tenant.TenantId && !s.IsDeleted)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync() ?? 0;

            var entity = new PageSection
            {
                TenantId = _tenant.TenantId,
                PageId = Id,
                SortOrder = (maxOrder <= 0 ? 0 : maxOrder) + 10,
                TypeKey = typeKey,
                ContentJson = "{}",
                IsDeleted = false,
                CreatedBy = GetUserIdOrEmpty(),
                CreatedAt = DateTime.UtcNow
            };

            _db.PageSections.Add(entity);
            await _db.SaveChangesAsync();

            TempData["Ok"] = "Section added.";
            return RedirectToPage("./Sections", new { id = Id });
        }

        // POST: Update a section (TypeKey + ContentJson)
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

            var typeKey = NormalizeTypeKey(Edit.TypeKey);

            var json = (Edit.ContentJson ?? string.Empty).Trim();

            // If empty, store "{}" as a safe default
            if (string.IsNullOrWhiteSpace(json))
                json = "{}";

            // Minimal JSON sanity check: object or array
            if (!LooksLikeJson(json))
            {
                TempData["Err"] = "ContentJson must be valid JSON (object/array).";
                return RedirectToPage("./Sections", new { id = Id });
            }

            entity.TypeKey = typeKey;
            entity.ContentJson = json;

            entity.UpdatedBy = GetUserIdOrEmpty();
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Ok"] = "Section updated.";
            return RedirectToPage("./Sections", new { id = Id });
        }

        // POST: Soft delete a section
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

        // POST: Reorder sections (AJAX)
        public async Task<IActionResult> OnPostReorderAsync([FromBody] PageSectionReorderVm vm)
        {
            if (!_tenant.IsResolved) return NotFound();

            if (Id <= 0) return NotFound();

            if (vm?.OrderedIds is null || vm.OrderedIds.Count == 0)
                return BadRequest(new { ok = false, message = "No IDs provided." });

            // Load current sections for this page/tenant
            var sections = await _db.PageSections
                .Where(s => s.PageId == Id && s.TenantId == _tenant.TenantId && !s.IsDeleted)
                .ToListAsync();

            if (sections.Count == 0)
                return BadRequest(new { ok = false, message = "No sections to reorder." });

            var currentIds = sections.Select(s => s.Id).ToHashSet();

            // Validate IDs belong to this page
            foreach (var sid in vm.OrderedIds)
            {
                if (!currentIds.Contains(sid))
                    return BadRequest(new { ok = false, message = "Invalid section id in reorder list." });
            }

            // Ensure the reorder list covers all sections
            if (vm.OrderedIds.Count != sections.Count)
                return BadRequest(new { ok = false, message = "Reorder list does not match section count." });

            // Apply new order: 10,20,30...
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

        private static string NormalizeTypeKey(string typeKey)
        {
            typeKey = (typeKey ?? string.Empty).Trim().ToLowerInvariant();

            // keep only a-z, 0-9, hyphen, underscore
            typeKey = System.Text.RegularExpressions.Regex.Replace(typeKey, @"[^a-z0-9\-_]", "");
            typeKey = typeKey.Trim('-', '_');

            return string.IsNullOrWhiteSpace(typeKey) ? "text" : typeKey;
        }

        private static bool LooksLikeJson(string value)
        {
            var v = (value ?? string.Empty).Trim();
            return (v.StartsWith("{") && v.EndsWith("}")) || (v.StartsWith("[") && v.EndsWith("]"));
        }

        private Guid GetUserIdOrEmpty()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }
}
