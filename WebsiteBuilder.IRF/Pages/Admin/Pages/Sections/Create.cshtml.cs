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
    public class CreateModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ISectionRegistry _registry;
        private readonly ISectionValidationService _validation;

        public CreateModel(
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

        public async Task<IActionResult> OnGetAsync(int pageId, string? typeKey = null)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            // Tenant guard: page must exist under tenant
            var pageExists = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p => p.Id == pageId && p.TenantId == _tenant.TenantId && !p.IsDeleted);

            if (!pageExists)
                return NotFound();

            Section.PageId = pageId;
            Section.SortOrder = 0;

            // Determine initial type by name (typeKey)
            var initialTypeKey = !string.IsNullOrWhiteSpace(typeKey)
                ? typeKey.Trim()
                : (_registry.All.FirstOrDefault()?.TypeKey ?? "Text");

            // Resolve SectionType row by Name
            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .OrderBy(st => st.Id)
                .FirstOrDefaultAsync(st => st.Name == initialTypeKey);

            // If not found, fallback to "Text" (must exist by migration)
            if (sectionType == null)
            {
                sectionType = await _db.SectionTypes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(st => st.Name == "Text");
            }

            if (sectionType == null)
                return BadRequest("SectionTypes table is missing required seed rows (e.g., 'Text').");

            Section.SectionTypeId = sectionType.Id;

            // Default JSON from registry (if known); otherwise {}
            var defaultJson = "{}";
            if (_registry.TryGet(sectionType.Name, out var def))
                defaultJson = def.DefaultJson ?? "{}";

            Section.SettingsJson = defaultJson;

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

            // Tenant guard: page must exist under tenant
            var pageExists = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p => p.Id == Section.PageId && p.TenantId == _tenant.TenantId && !p.IsDeleted);

            if (!pageExists)
            {
                ModelState.AddModelError(nameof(Section.PageId), "Page not found.");
                return Page();
            }

            // Resolve SectionType from DB
            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == Section.SectionTypeId);

            if (sectionType == null)
            {
                ModelState.AddModelError(nameof(Section.SectionTypeId), "Unknown section type.");
                return Page();
            }

            // Guard: only allow types known by registry (renderer + validator)
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

            // Set tenant + audit fields
            Section.TenantId = _tenant.TenantId;
            Section.IsDeleted = false;

            var userId = GetUserIdOrEmpty();
            Section.CreatedAt = DateTime.UtcNow;
            Section.CreatedBy = userId;

            _db.PageSections.Add(Section);
            await _db.SaveChangesAsync();

            return RedirectToPage("/Admin/Pages/Sections", new { id = Section.PageId });
        }

        // Live JSON validation endpoint (AJAX)
        public async Task<IActionResult> OnPostValidateJsonAsync([FromForm] int sectionTypeId, [FromForm] string? settingsJson)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { isValid = false, errors = new[] { "Tenant not resolved." } });

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

            // Guard: unknown type -> invalid (must exist in registry)
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

        private Guid GetUserIdOrEmpty()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }
}
