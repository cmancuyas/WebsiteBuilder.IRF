using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Sections.Settings;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages;

public sealed class SectionsModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISectionValidationService _sectionValidation;

    public SectionsModel(DataContext db, ITenantContext tenant, ISectionValidationService sectionValidation)
    {
        _db = db;
        _tenant = tenant;
        _sectionValidation = sectionValidation;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; } // PageId

    public string PageTitle { get; private set; } = "";
    public string PageSlug { get; private set; } = "";
    public string? Banner { get; private set; }

    public bool HasDraft { get; private set; }
    public int? DraftRevisionId { get; private set; }

    public sealed record Row(
        int Id,
        int SortOrder,
        int SectionTypeId,
        string SectionTypeName,
        string SectionTypeKey,
        string SettingsJson,
        object? TypedModel
    );

    public List<Row> Items { get; private set; } = new();

    public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

    public sealed class AddSectionModel
    {
        [Required]
        public int SectionTypeId { get; set; }
    }

    [BindProperty]
    public AddSectionModel Add { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAddSectionAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();

        if (page.DraftRevisionId is null)
        {
            TempData["Error"] = "No draft revision exists. Restore a draft first.";
            return RedirectToPage(new { id = Id });
        }

        if (!ModelState.IsValid)
            return await LoadAsync(ct);

        var st = await _db.SectionTypes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Add.SectionTypeId && !x.IsDeleted, ct);

        if (st is null)
        {
            TempData["Error"] = "Invalid section type.";
            return RedirectToPage(new { id = Id });
        }

        var maxSort = await _db.PageRevisionSections
            .AsNoTracking()
            .Where(s => s.TenantId == _tenant.TenantId && s.PageRevisionId == page.DraftRevisionId && !s.IsDeleted)
            .MaxAsync(s => (int?)s.SortOrder, ct) ?? 0;

        var section = new PageRevisionSection
        {
            TenantId = _tenant.TenantId,
            PageRevisionId = page.DraftRevisionId.Value,
            SectionTypeId = st.Id,
            SortOrder = maxSort + 1,
            SettingsJson = "{}"
        };

        _db.PageRevisionSections.Add(section);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Section added.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostUpdateJsonAsync(int sectionId, string? settingsJson, CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");

        var page = await _db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();
        if (page.DraftRevisionId is null)
        {
            TempData["Error"] = "No draft revision exists. Restore a draft first.";
            return RedirectToPage(new { id = Id });
        }

        var section = await _db.PageRevisionSections
            .Include(s => s.SectionType)
            .FirstOrDefaultAsync(s =>
                s.Id == sectionId &&
                s.TenantId == _tenant.TenantId &&
                s.PageRevisionId == page.DraftRevisionId &&
                !s.IsDeleted, ct);

        if (section is null) return NotFound();

        var json = string.IsNullOrWhiteSpace(settingsJson) ? "{}" : settingsJson.Trim();
        var typeKey = section.SectionType?.Key ?? "";

        var result = await _sectionValidation.ValidateAsync(typeKey, json);
        if (!result.IsValid)
        {
            TempData["Error"] = string.Join(" | ", result.Errors);
            return RedirectToPage(new { id = Id });
        }

        section.SettingsJson = json;
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Section JSON updated.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostUpdateTypedAsync(int sectionId, string typeKey, CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");

        var page = await _db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();
        if (page.DraftRevisionId is null)
        {
            TempData["Error"] = "No draft revision exists. Restore a draft first.";
            return RedirectToPage(new { id = Id });
        }

        var section = await _db.PageRevisionSections
            .Include(s => s.SectionType)
            .FirstOrDefaultAsync(s =>
                s.Id == sectionId &&
                s.TenantId == _tenant.TenantId &&
                s.PageRevisionId == page.DraftRevisionId &&
                !s.IsDeleted, ct);

        if (section is null) return NotFound();

        if (!string.Equals(typeKey, section.SectionType?.Key, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Section type mismatch.";
            return RedirectToPage(new { id = Id });
        }

        // Build JSON from form fields (simple + reliable)
        var normalized = (typeKey ?? "").Trim().ToLowerInvariant();

        string json = normalized switch
        {
            "hero" => SectionJson.Serialize(new HeroSettings
            {
                Heading = Request.Form["Hero.Heading"],
                Subheading = Request.Form["Hero.Subheading"],
                CtaText = Request.Form["Hero.CtaText"],
                CtaUrl = Request.Form["Hero.CtaUrl"],
                BackgroundImageAssetId = int.TryParse(Request.Form["Hero.BackgroundImageAssetId"], out var bg) ? bg : null
            }),

            "text" => SectionJson.Serialize(new TextSettings
            {
                Title = Request.Form["Text.Title"],
                Body = Request.Form["Text.Body"],
                Align = Request.Form["Text.Align"]
            }),

            "gallery" => SectionJson.Serialize(new GallerySettings
            {
                Layout = Request.Form["Gallery.Layout"],
                ImageAssetIds = (Request.Form["Gallery.ImageAssetIds"].ToString() ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? (int?)v : null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .ToList()
            }),

            _ => section.SettingsJson ?? "{}"
        };

        var result = await _sectionValidation.ValidateAsync(section.SectionType!.Key, json);
        if (!result.IsValid)
        {
            TempData["Error"] = string.Join(" | ", result.Errors);
            return RedirectToPage(new { id = Id });
        }

        section.SettingsJson = json;
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Section updated.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int sectionId, CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");

        var page = await _db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();
        if (page.DraftRevisionId is null)
        {
            TempData["Error"] = "No draft revision exists. Restore a draft first.";
            return RedirectToPage(new { id = Id });
        }

        var section = await _db.PageRevisionSections
            .FirstOrDefaultAsync(s =>
                s.Id == sectionId &&
                s.TenantId == _tenant.TenantId &&
                s.PageRevisionId == page.DraftRevisionId &&
                !s.IsDeleted, ct);

        if (section is null) return NotFound();

        _db.PageRevisionSections.Remove(section);
        await _db.SaveChangesAsync(ct);

        await RepackSortOrderAsync(page.DraftRevisionId.Value, ct);

        TempData["Success"] = "Section deleted.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostMoveUpAsync(int sectionId, CancellationToken ct)
        => await MoveAsync(sectionId, -1, ct);

    public async Task<IActionResult> OnPostMoveDownAsync(int sectionId, CancellationToken ct)
        => await MoveAsync(sectionId, +1, ct);

    private async Task<IActionResult> MoveAsync(int sectionId, int direction, CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");

        var page = await _db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();
        if (page.DraftRevisionId is null)
        {
            TempData["Error"] = "No draft revision exists. Restore a draft first.";
            return RedirectToPage(new { id = Id });
        }

        var list = await _db.PageRevisionSections
            .Where(s => s.TenantId == _tenant.TenantId && s.PageRevisionId == page.DraftRevisionId && !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        var idx = list.FindIndex(x => x.Id == sectionId);
        if (idx < 0) return NotFound();

        var swapIdx = idx + direction;
        if (swapIdx < 0 || swapIdx >= list.Count)
            return RedirectToPage(new { id = Id });

        (list[idx].SortOrder, list[swapIdx].SortOrder) = (list[swapIdx].SortOrder, list[idx].SortOrder);

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Section order updated.";
        return RedirectToPage(new { id = Id });
    }

    private async Task RepackSortOrderAsync(int draftRevisionId, CancellationToken ct)
    {
        var list = await _db.PageRevisionSections
            .Where(s => s.TenantId == _tenant.TenantId && s.PageRevisionId == draftRevisionId && !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        var i = 1;
        foreach (var s in list)
            s.SortOrder = i++;

        await _db.SaveChangesAsync(ct);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.Id == Id &&
                p.TenantId == _tenant.TenantId &&
                !p.IsDeleted,
                ct);

        if (page is null) return NotFound();

        PageTitle = page.Title;
        PageSlug = page.Slug;

        DraftRevisionId = page.DraftRevisionId;
        HasDraft = DraftRevisionId is not null;

        SectionTypeOptions = await _db.SectionTypes
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Select(t => new SelectListItem(t.Name, t.Id.ToString()))
            .ToListAsync(ct);

        if (!HasDraft)
        {
            Items = new();
        }
        else
        {
            var sections = await _db.PageRevisionSections
                .AsNoTracking()
                .Include(s => s.SectionType)
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == DraftRevisionId &&
                    !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ToListAsync(ct);

            Items = sections.Select(s =>
            {
                var key = s.SectionType!.Key;
                var json = s.SettingsJson ?? "{}";

                object? typed = key.ToLowerInvariant() switch
                {
                    "hero" => SectionJson.Deserialize<HeroSettings>(json),
                    "text" => SectionJson.Deserialize<TextSettings>(json),
                    "gallery" => SectionJson.Deserialize<GallerySettings>(json),
                    _ => null
                };

                return new Row(
                    s.Id,
                    s.SortOrder,
                    s.SectionTypeId,
                    s.SectionType.Name,
                    s.SectionType.Key,
                    json,
                    typed
                );
            }).ToList();
        }

        if (TempData.TryGetValue("Success", out var ok))
            Banner = ok?.ToString();

        if (TempData.TryGetValue("Error", out var err))
            Banner = err?.ToString();

        return Page();
    }
}
