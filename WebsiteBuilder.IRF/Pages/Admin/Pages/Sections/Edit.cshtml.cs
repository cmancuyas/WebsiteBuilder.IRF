using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages.Sections
{
    public class EditModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ISectionRegistry _registry;
        private readonly ISectionValidationService _validation;

        public EditModel(
            DataContext db,
            ITenantContext tenant,
            ISectionRegistry registry,
            ISectionValidationService validation)
        {
            _db = db;
            _tenant = tenant;
            _registry = registry;
            _validation = validation;
        }

        [BindProperty]
        public PageSection Section { get; set; } = new();

        public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            // Load section with tenant safety
            var entity = await _db.PageSections
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.TenantId == _tenant.TenantId &&
                    !x.IsDeleted);

            if (entity == null)
                return NotFound();

            Section = entity;

            await BuildSectionTypeOptionsAsync(Section.SectionTypeId);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!_tenant.IsResolved)
                return NotFound();

            await BuildSectionTypeOptionsAsync(Section.SectionTypeId);

            if (!ModelState.IsValid)
                return Page();

            // Load existing (tracked) with tenant safety
            var existing = await _db.PageSections
                .FirstOrDefaultAsync(x =>
                    x.Id == Section.Id &&
                    x.TenantId == _tenant.TenantId &&
                    !x.IsDeleted);

            if (existing == null)
                return NotFound();

            // Resolve SectionType from DB (single source of truth)
            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == Section.SectionTypeId);

            if (sectionType == null)
            {
                ModelState.AddModelError(nameof(Section.SectionTypeId), "Unknown section type.");
                return Page();
            }

            // Guard: must be supported by registry (renderer/validator availability)
            if (!_registry.TryGet(sectionType.Name, out _))
            {
                ModelState.AddModelError(nameof(Section.SectionTypeId), "Section type is not supported by the registry.");
                return Page();
            }

            var json = (Section.SettingsJson ?? "{}").Trim();
            var result = await _validation.ValidateAsync(sectionType.Name, json);

            if (!result.IsValid)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(nameof(Section.SettingsJson), error);

                return Page();
            }

            // Update only canonical mutable fields
            existing.SectionTypeId = Section.SectionTypeId;
            existing.SortOrder = Section.SortOrder;
            existing.SettingsJson = Section.SettingsJson;

            // audit
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = GetUserIdOrNull();

            await _db.SaveChangesAsync();

            // Your list page route varies by your implementation.
            // In your previous code you used pageId = existing.PageId; keep the same if your model has PageId.
            // If PageId exists on PageSection, redirect there; otherwise redirect to a generic sections index.
            if (HasPageId(existing, out var pageId))
                return RedirectToPage("/Admin/Pages/Sections", new { id = pageId });

            return RedirectToPage("/Admin/Pages/Sections");
        }

        // Live JSON validation endpoint (AJAX)
        public async Task<IActionResult> OnPostValidateJsonAsync([FromForm] int sectionTypeId, [FromForm] string? settingsJson)
        {
            if (!_tenant.IsResolved)
            {
                return new JsonResult(new
                {
                    isValid = false,
                    errors = new[] { "Tenant not resolved." }
                });
            }

            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == sectionTypeId);

            if (sectionType == null)
            {
                return new JsonResult(new
                {
                    isValid = false,
                    errors = new[] { "Unknown section type." }
                });
            }

            if (!_registry.TryGet(sectionType.Name, out _))
            {
                return new JsonResult(new
                {
                    isValid = false,
                    errors = new[] { "Section type is not supported by the registry." }
                });
            }

            var result = await _validation.ValidateAsync(sectionType.Name, settingsJson);

            return new JsonResult(new
            {
                isValid = result.IsValid,
                errors = result.Errors?.ToArray() ?? Array.Empty<string>()
            });
        }

        private async Task BuildSectionTypeOptionsAsync(int selectedSectionTypeId)
        {
            var types = await _db.SectionTypes
                .AsNoTracking()
                .OrderBy(st => st.Name)
                .ToListAsync();

            SectionTypeOptions = types
                .Select(st => new SelectListItem
                {
                    Value = st.Id.ToString(),
                    Text = st.Name,
                    Selected = st.Id == selectedSectionTypeId
                })
                .ToList();
        }

        private Guid? GetUserIdOrNull()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }

        // Small helper to avoid compile break if PageId is not present in your current PageSection shape.
        private static bool HasPageId(PageSection section, out int pageId)
        {
            pageId = 0;
            var prop = section.GetType().GetProperty("PageId");
            if (prop == null) return false;

            var val = prop.GetValue(section);
            if (val is int i)
            {
                pageId = i;
                return true;
            }

            return false;
        }
    }
}
