using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages.Sections
{
    public class EditModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ISectionRegistry _registry;
        private readonly ISectionValidationService _validation;

        public EditModel(
            DataContext db,
            ISectionRegistry registry,
            ISectionValidationService validation)
        {
            _db = db;
            _registry = registry;
            _validation = validation;
        }

        [BindProperty]
        public PageSection Section { get; set; } = new();

        public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var entity = await _db.PageSections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entity == null)
                return NotFound();

            Section = entity;

            BuildSectionTypeOptions(Section.TypeKey);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            BuildSectionTypeOptions(Section.TypeKey);

            if (!ModelState.IsValid)
                return Page();

            // Guard: only allow known types
            if (!_registry.TryGet(Section.TypeKey, out _))
            {
                ModelState.AddModelError(nameof(Section.TypeKey), "Unknown section type.");
                return Page();
            }

            var result = await _validation.ValidateAsync(Section.TypeKey, Section.ContentJson);

            if (!result.IsValid)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(nameof(Section.ContentJson), error);

                return Page();
            }

            var existing = await _db.PageSections.FirstOrDefaultAsync(x => x.Id == Section.Id);
            if (existing == null)
                return NotFound();

            // Update only mutable fields (keep tenant/audit consistent with your base model rules)
            existing.TypeKey = Section.TypeKey;
            existing.SortOrder = Section.SortOrder;
            existing.ContentJson = Section.ContentJson;

            await _db.SaveChangesAsync();

            return RedirectToPage("/Admin/Pages/Sections", new { pageId = existing.PageId });
        }

        // Live JSON validation endpoint (AJAX)
        public async Task<IActionResult> OnPostValidateJsonAsync([FromForm] string typeKey, [FromForm] string? contentJson)
        {
            if (!_registry.TryGet(typeKey, out _))
            {
                return new JsonResult(new
                {
                    isValid = false,
                    errors = new[] { "Unknown section type." }
                });
            }

            var result = await _validation.ValidateAsync(typeKey, contentJson);

            return new JsonResult(new
            {
                isValid = result.IsValid,
                errors = result.Errors?.ToArray() ?? Array.Empty<string>()
            });
        }

        private void BuildSectionTypeOptions(string? selectedTypeKey)
        {
            var selected = (selectedTypeKey ?? string.Empty).Trim();

            SectionTypeOptions = _registry.All
                .OrderBy(x => x.DisplayName)
                .Select(x => new SelectListItem
                {
                    Value = x.TypeKey,
                    Text = x.DisplayName,
                    Selected = string.Equals(x.TypeKey, selected, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }
    }
}
