using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages.Sections
{
    public class CreateModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ISectionRegistry _registry;
        private readonly ISectionValidationService _validation;

        public CreateModel(
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

        public IActionResult OnGet(int pageId, string? typeKey = null)
        {
            Section.PageId = pageId;
            Section.SortOrder = 0;

            // Default to first registry entry if not provided
            var initialType = !string.IsNullOrWhiteSpace(typeKey)
                ? typeKey!.Trim()
                : _registry.All.FirstOrDefault()?.TypeKey ?? "Text";

            Section.TypeKey = initialType;

            BuildSectionTypeOptions(initialType);
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

            _db.PageSections.Add(Section);
            await _db.SaveChangesAsync();

            return RedirectToPage("/Admin/Pages/Sections", new { pageId = Section.PageId });
        }

        // Live JSON validation endpoint (AJAX)
        public async Task<IActionResult> OnPostValidateJsonAsync([FromForm] string typeKey, [FromForm] string? contentJson)
        {
            // Guard: unknown type -> invalid
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
