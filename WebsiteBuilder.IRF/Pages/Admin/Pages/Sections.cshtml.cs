using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Razor;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Sections.Settings;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages.SectionSettings;
using WebsiteBuilder.Models;
using Page = WebsiteBuilder.Models.Page;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages;

public sealed class SectionsModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISectionRegistry _sections;
    private readonly IPageRevisionSectionService _pageRevisionSectionService;
    private readonly IRazorPartialRenderer _partial;
    private readonly ISectionValidationService _sectionValidation;

    public SectionsModel(
        DataContext db,
        ITenantContext tenant,
        ISectionRegistry sections,
        IPageRevisionSectionService pageRevisionSectionService,
        IRazorPartialRenderer partial,
        ISectionValidationService sectionValidation)
    {
        _db = db;
        _tenant = tenant;
        _sections = sections;
        _pageRevisionSectionService = pageRevisionSectionService;
        _partial = partial;
        _sectionValidation = sectionValidation;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; } // PageId

    public string PageTitle { get; private set; } = "";
    public string PageSlug { get; private set; } = "";

    public int? DraftRevisionId { get; private set; }
    public bool HasDraft => DraftRevisionId is not null;

    public string DraftRevisionRowVersionBase64 { get; private set; } = "";
    public string? Banner { get; private set; }

    public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

    public sealed class AddSectionInput
    {
        public int SectionTypeId { get; set; }
    }

    [BindProperty]
    public AddSectionInput Add { get; set; } = new();

    public sealed class DeleteRevisionSectionRequest
    {
        public int RevisionSectionId { get; init; }
        public string? DraftRevisionRowVersion { get; init; } // base64
    }

    public sealed class AddRevisionSectionRequest
    {
        public int PageId { get; init; }
        public int SectionTypeId { get; init; }
        public int? InsertAfterRevisionSectionId { get; init; }
        public bool InsertAtTop { get; init; } = false;
        public string? DraftRevisionRowVersion { get; init; } // base64
    }

    public sealed class ReorderRevisionSectionsRequest
    {
        public int PageId { get; init; }
        public List<int> OrderedRevisionSectionIds { get; init; } = new();
        public string? DraftRevisionRowVersion { get; init; } // base64
    }

    public sealed class SectionItemVm
    {
        public int Id { get; init; }
        public int SortOrder { get; init; }
        public int SectionTypeId { get; init; }
        public string SectionTypeKey { get; init; } = "";
        public string SectionTypeName { get; init; } = "";
        public string SettingsJson { get; init; } = "{}";
        public object? TypedModel { get; init; }
    }

    public List<SectionItemVm> Items { get; private set; } = new();
    public List<SectionRowRenderVm> SectionRows { get; private set; } = new();

    // =========================
    // GET
    // =========================
    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    // =========================
    // AJAX: ADD REVISION SECTION (Optimistic Concurrency)
    // POST: /Admin/Pages/Sections/{id}?handler=AddRevisionSection
    // =========================
    public async Task<IActionResult> OnPostAddRevisionSectionAsync(
        int id,
        [FromBody] AddRevisionSectionRequest req,
        CancellationToken ct = default)
    {
        if (!_tenant.IsResolved)
            return new JsonResult(new { ok = false, error = "Tenant not resolved." }) { StatusCode = 404 };

        if (req is null || req.SectionTypeId <= 0)
            return BadRequest(new { ok = false, error = "Invalid request." });

        if (req.PageId <= 0 || req.PageId != id)
            return BadRequest(new { ok = false, error = "Mismatched PageId." });

        var page = await _db.Pages
            .AsNoTracking()
            .Where(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted)
            .Select(p => new { p.Id, p.DraftRevisionId })
            .FirstOrDefaultAsync(ct);

        if (page is null)
            return NotFound(new { ok = false, error = "Page not found." });

        if (page.DraftRevisionId is null)
            return BadRequest(new { ok = false, error = "No draft exists. Restore or create a draft first." });

        var draftRevisionId = page.DraftRevisionId.Value;

        var st = await _db.Set<SectionType>()
            .AsNoTracking()
            .Where(x => x.Id == req.SectionTypeId && !x.IsDeleted)
            .Select(x => new { x.Id, x.Key })
            .FirstOrDefaultAsync(ct);

        if (st is null)
            return BadRequest(new { ok = false, error = "Invalid SectionTypeId." });

        // ✅ tracked draft revision for concurrency + "touch"
        var draft = await _db.PageRevisions
            .FirstOrDefaultAsync(r =>
                r.Id == draftRevisionId &&
                r.TenantId == _tenant.TenantId &&
                r.PageId == id &&
                !r.IsDeleted, ct);

        if (draft is null)
            return NotFound(new { ok = false, error = "Draft revision not found." });

        try
        {
            ApplyOptimisticConcurrency(draft, req.DraftRevisionRowVersion);

            // Load current sections (tracked because we shift SortOrder)
            var existing = await _db.PageRevisionSections
                .Where(s => s.TenantId == _tenant.TenantId
                            && s.PageRevisionId == draftRevisionId
                            && !s.IsDeleted)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToListAsync(ct);

            int newSortOrder;

            if (req.InsertAtTop)
            {
                foreach (var s in existing) s.SortOrder += 1;
                newSortOrder = 1;
            }
            else if (req.InsertAfterRevisionSectionId.HasValue)
            {
                var afterId = req.InsertAfterRevisionSectionId.Value;
                var after = existing.FirstOrDefault(x => x.Id == afterId);
                if (after is null)
                    return BadRequest(new { ok = false, error = "InsertAfter section not found." });

                newSortOrder = after.SortOrder + 1;

                foreach (var s in existing)
                    if (s.SortOrder >= newSortOrder)
                        s.SortOrder += 1;
            }
            else
            {
                newSortOrder = existing.Count == 0 ? 1 : existing.Max(x => x.SortOrder) + 1;
            }

            var typeKey = (st.Key ?? "").Trim();

            var newSection = new PageRevisionSection
            {
                TenantId = _tenant.TenantId,
                PageRevisionId = draftRevisionId,
                SectionTypeId = st.Id,
                SortOrder = newSortOrder,
                SettingsJson = GetDefaultJsonByKey(typeKey)
            };

            _db.PageRevisionSections.Add(newSection);

            // ✅ bump draft rowversion for any section mutation
            draft.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, draftRevisionId, ct);

            var created = await _db.PageRevisionSections
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == newSection.Id &&
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted, ct);

            if (created is null)
                return new JsonResult(new { ok = false, error = "Failed to load created section." }) { StatusCode = 500 };

            var title = !string.IsNullOrWhiteSpace(typeKey) ? typeKey : "Section";
            var editorPartialPath = "Shared/Sections/_Text";

            if (!string.IsNullOrWhiteSpace(typeKey) && _sections.TryGet(typeKey, out var def))
            {
                title = def.DisplayName;
                editorPartialPath = def.PartialViewPath;
            }

            var rowVm = new SectionRowRenderVm
            {
                RevisionSectionId = created.Id,
                SectionTypeId = created.SectionTypeId,
                Title = title,
                CollapseId = $"sec-editor-{created.Id}",
                EditorPartialPath = editorPartialPath,
                IsEditable = true,
                Section = created
            };

            var html = await _partial.RenderPartialAsync(
                "/Pages/Admin/Pages/Partials/_PageRevisionSectionRow.cshtml",
                rowVm,
                HttpContext);

            return new JsonResult(new
            {
                ok = true,
                revisionSectionId = created.Id,
                title,
                html,
                draftRevisionRowVersion = ToBase64(draft.RowVersion)
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message }) { StatusCode = 400 };
        }
    }

    // =========================
    // AJAX: DELETE (Optimistic Concurrency)
    // POST: /Admin/Pages/Sections/{id}?handler=DeleteRevisionSection
    // =========================
    public async Task<IActionResult> OnPostDeleteRevisionSectionAsync(
        int id,
        [FromBody] DeleteRevisionSectionRequest req,
        CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return new JsonResult(new { ok = false, error = "Tenant not resolved." }) { StatusCode = 404 };

        if (req is null || req.RevisionSectionId <= 0)
            return BadRequest(new { ok = false, error = "Invalid request." });

        var page = await _db.Pages
            .AsNoTracking()
            .Where(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted)
            .Select(p => new { p.Id, p.DraftRevisionId })
            .FirstOrDefaultAsync(ct);

        if (page is null)
            return NotFound(new { ok = false, error = "Page not found." });

        if (page.DraftRevisionId is null)
            return BadRequest(new { ok = false, error = "No draft exists." });

        var draftRevisionId = page.DraftRevisionId.Value;

        var draft = await _db.PageRevisions
            .FirstOrDefaultAsync(r =>
                r.Id == draftRevisionId &&
                r.TenantId == _tenant.TenantId &&
                r.PageId == id &&
                !r.IsDeleted, ct);

        if (draft is null)
            return NotFound(new { ok = false, error = "Draft revision not found." });

        try
        {
            ApplyOptimisticConcurrency(draft, req.DraftRevisionRowVersion);

            var section = await _db.PageRevisionSections
                .FirstOrDefaultAsync(s =>
                    s.Id == req.RevisionSectionId &&
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted, ct);

            if (section is null)
                return NotFound(new { ok = false, error = "Section not found." });

            section.IsDeleted = true;

            // bump draft rowversion
            draft.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, draftRevisionId, ct);

            return new JsonResult(new
            {
                ok = true,
                draftRevisionRowVersion = ToBase64(draft.RowVersion)
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message }) { StatusCode = 400 };
        }
    }

    // =========================
    // AJAX: REORDER (HARDENED + Optimistic Concurrency)
    // POST: /Admin/Pages/Sections/{id}?handler=ReorderRevisionSections
    // =========================
    public async Task<IActionResult> OnPostReorderRevisionSectionsAsync(
        int id,
        [FromBody] ReorderRevisionSectionsRequest req,
        CancellationToken ct = default)
    {
        if (!_tenant.IsResolved)
            return new JsonResult(new { ok = false, error = "Tenant not resolved." }) { StatusCode = 404 };

        if (req is null || req.PageId <= 0 || req.PageId != id)
            return BadRequest(new { ok = false, error = "Invalid request." });

        if (req.OrderedRevisionSectionIds is null || req.OrderedRevisionSectionIds.Count == 0)
            return BadRequest(new { ok = false, error = "No section IDs provided." });

        var ordered = req.OrderedRevisionSectionIds.ToList();
        if (ordered.Any(x => x <= 0))
            return BadRequest(new { ok = false, error = "Invalid section IDs." });

        var dupes = ordered.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            return BadRequest(new { ok = false, error = "Duplicate section IDs.", duplicates = dupes });

        var page = await _db.Pages
            .AsNoTracking()
            .Where(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted)
            .Select(p => new { p.Id, p.DraftRevisionId })
            .FirstOrDefaultAsync(ct);

        if (page is null)
            return NotFound(new { ok = false, error = "Page not found." });

        if (page.DraftRevisionId is null)
            return BadRequest(new { ok = false, error = "No draft exists. Restore or create a draft first." });

        var draftRevisionId = page.DraftRevisionId.Value;

        var draft = await _db.PageRevisions
            .FirstOrDefaultAsync(r =>
                r.Id == draftRevisionId &&
                r.TenantId == _tenant.TenantId &&
                r.PageId == id &&
                !r.IsDeleted, ct);

        if (draft is null)
            return NotFound(new { ok = false, error = "Draft revision not found." });

        // authoritative set
        var existingIds = await _db.PageRevisionSections
            .AsNoTracking()
            .Where(s => s.TenantId == _tenant.TenantId
                        && s.PageRevisionId == draftRevisionId
                        && !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var missingFromClient = existingIds.Except(ordered).ToList();
        var extraFromClient = ordered.Except(existingIds).ToList();

        if (missingFromClient.Count > 0 || extraFromClient.Count > 0)
        {
            return BadRequest(new
            {
                ok = false,
                error = "Reorder list does not match current draft sections.",
                missingFromClient,
                extraFromClient
            });
        }

        try
        {
            ApplyOptimisticConcurrency(draft, req.DraftRevisionRowVersion);

            var tracked = await _db.PageRevisionSections
                .Where(s => s.TenantId == _tenant.TenantId
                            && s.PageRevisionId == draftRevisionId
                            && !s.IsDeleted)
                .ToListAsync(ct);

            var byId = tracked.ToDictionary(x => x.Id);

            for (int i = 0; i < ordered.Count; i++)
                byId[ordered[i]].SortOrder = i + 1;

            // bump draft rowversion
            draft.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, draftRevisionId, ct);

            return new JsonResult(new
            {
                ok = true,
                draftRevisionRowVersion = ToBase64(draft.RowVersion)
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message }) { StatusCode = 400 };
        }
    }

    // =========================
    // LEGACY POSTBACK ADD (optional)
    // =========================
    public async Task<IActionResult> OnPostAddSectionAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .FirstOrDefaultAsync(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

        if (page is null) return NotFound();
        if (page.DraftRevisionId is null)
        {
            TempData["Error"] = "No draft revision exists. Restore a draft before editing sections.";
            return RedirectToPage(new { id = Id });
        }

        if (Add.SectionTypeId <= 0)
        {
            ModelState.AddModelError(nameof(Add.SectionTypeId), "Please select a section type.");
            return await LoadAsync(ct);
        }

        var st = await _db.Set<SectionType>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Add.SectionTypeId, ct);

        if (st is null)
        {
            ModelState.AddModelError(nameof(Add.SectionTypeId), "Invalid section type.");
            return await LoadAsync(ct);
        }

        var maxSort = await _db.PageRevisionSections
            .Where(s => s.TenantId == _tenant.TenantId && s.PageRevisionId == page.DraftRevisionId.Value && !s.IsDeleted)
            .MaxAsync(s => (int?)s.SortOrder, ct) ?? 0;

        _db.PageRevisionSections.Add(new PageRevisionSection
        {
            TenantId = _tenant.TenantId,
            PageRevisionId = page.DraftRevisionId.Value,
            SectionTypeId = st.Id,
            SortOrder = maxSort + 1,
            SettingsJson = GetDefaultJsonByKey((st.Key ?? "").Trim())
        });

        await _db.SaveChangesAsync(ct);

        await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, page.DraftRevisionId.Value, ct);

        TempData["Success"] = "Section added.";
        return RedirectToPage(new { id = Id });
    }
    public async Task<IActionResult> OnPostSaveSectionSettingsAsync(
        [FromBody] SaveSectionSettingsRequestVm req,
        CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");
        if (req is null) return BadRequest();

        var page = await _db.Pages
            .AsNoTracking()
            .Where(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted)
            .Select(p => new { p.Id, p.DraftRevisionId })
            .FirstOrDefaultAsync(ct);

        if (page == null) return NotFound();
        if (page.DraftRevisionId is null) return BadRequest("No draft revision exists.");

        var draft = await _db.PageRevisions
            .FirstOrDefaultAsync(r =>
                r.Id == page.DraftRevisionId.Value &&
                r.TenantId == _tenant.TenantId &&
                r.PageId == Id &&
                !r.IsDeleted, ct);

        if (draft == null) return NotFound("Draft revision not found.");

        var section = await _db.PageRevisionSections
            .Include(s => s.SectionType)
            .FirstOrDefaultAsync(s =>
                s.Id == req.SectionId &&
                s.TenantId == _tenant.TenantId &&
                !s.IsDeleted &&
                s.PageRevisionId == page.DraftRevisionId.Value, ct);

        if (section == null) return NotFound("Section not found.");
        if (section.SectionType == null) return BadRequest("Section type not found.");

        var typeKey = (section.SectionType.Key ?? "").Trim();
        if (string.IsNullOrWhiteSpace(typeKey))
            return BadRequest(new { ok = false, errors = new[] { "SectionType.Key is required." } });

        var json = string.IsNullOrWhiteSpace(req.SettingsJson) ? "{}" : req.SettingsJson;

        try
        {
            ApplyOptimisticConcurrency(draft, req.DraftRevisionRowVersion);

            // Optional (recommended): section-level concurrency
            var secToken = FromBase64(req.SectionRowVersion);
            if (secToken is not null && secToken.Length > 0)
                _db.Entry(section).Property(x => x.RowVersion).OriginalValue = secToken;

            var validation = await _sectionValidation.ValidateAsync(typeKey, json);
            if (!validation.IsValid)
                return BadRequest(new { ok = false, errors = validation.Errors });

            section.SettingsJson = json;
            draft.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new
            {
                ok = true,
                draftRevisionRowVersion = ToBase64(draft.RowVersion),
                sectionRowVersion = ToBase64(section.RowVersion)
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message }) { StatusCode = 400 };
        }
    }


    // =========================
    // LOAD
    // =========================
    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved) return NotFound("Tenant not resolved.");

        var page = await _db.Pages
            .AsNoTracking()
            .Include(p => p.DraftRevision).ThenInclude(r => r!.Sections)
            .Where(p => p.Id == Id && p.TenantId == _tenant.TenantId && !p.IsDeleted)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.DraftRevisionId,
                DraftRowVersion = p.DraftRevision != null ? p.DraftRevision.RowVersion : null,
                Sections = p.DraftRevision != null ? p.DraftRevision.Sections : null
            })
            .FirstOrDefaultAsync(ct);

        if (page is null) return NotFound();

        PageTitle = page.Title;
        PageSlug = page.Slug;
        DraftRevisionId = page.DraftRevisionId;
        DraftRevisionRowVersionBase64 = ToBase64(page.DraftRowVersion);

        SectionTypeOptions = await _db.Set<SectionType>()
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync(ct);

        Items = new();
        SectionRows = new();

        if (DraftRevisionId is not null)
        {
            var typeIds = (page.Sections ?? Array.Empty<PageRevisionSection>())
                .Select(s => s.SectionTypeId)
                .Distinct()
                .ToList();

            var types = await _db.Set<SectionType>()
                .AsNoTracking()
                .Where(st => !st.IsDeleted && typeIds.Contains(st.Id))
                .Select(st => new { st.Id, st.Key, st.Name })
                .ToDictionaryAsync(x => x.Id, x => x, ct);

            var draftSections = (page.Sections ?? Array.Empty<PageRevisionSection>())
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToList();

            foreach (var s in draftSections)
            {
                types.TryGetValue(s.SectionTypeId, out var st);

                var key = st?.Key?.Trim() ?? "";
                var name = st?.Name?.Trim() ?? key;

                Items.Add(new SectionItemVm
                {
                    Id = s.Id,
                    SortOrder = s.SortOrder,
                    SectionTypeId = s.SectionTypeId,
                    SectionTypeKey = key,
                    SectionTypeName = string.IsNullOrWhiteSpace(name) ? key : name,
                    SettingsJson = s.SettingsJson ?? "{}",
                    TypedModel = DeserializeTypedModel(key, s.SettingsJson)
                });

                var title = string.IsNullOrWhiteSpace(key) ? "Section" : key;
                var editorPartialPath = "Shared/Sections/_Unknown";

                if (!string.IsNullOrWhiteSpace(key) && _sections.TryGet(key, out var def))
                {
                    title = def.DisplayName;
                    editorPartialPath = def.PartialViewPath;
                }

                SectionRows.Add(new SectionRowRenderVm
                {
                    RevisionSectionId = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    Title = title,
                    CollapseId = $"sec-editor-{s.Id}",
                    EditorPartialPath = editorPartialPath,
                    IsEditable = true,
                    SectionRowVersionBase64 = (s.RowVersion is { Length: > 0 } rv)
                        ? Convert.ToBase64String(rv)
                        : "",
                    Section = s
                });

            }
        }

        if (TempData.TryGetValue("Success", out var ok)) Banner = ok?.ToString();
        if (TempData.TryGetValue("Error", out var err)) Banner = err?.ToString();

        return Page();
    }

    private static object? DeserializeTypedModel(string typeKey, string? json)
    {
        var safeJson = string.IsNullOrWhiteSpace(json) ? "{}" : json;

        try
        {
            if (typeKey.Equals("Hero", StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Deserialize<HeroSettings>(safeJson) ?? new HeroSettings();

            if (typeKey.Equals("Text", StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Deserialize<TextSettings>(safeJson) ?? new TextSettings();

            if (typeKey.Equals("Gallery", StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Deserialize<GallerySettings>(safeJson) ?? new GallerySettings();

            return null;
        }
        catch
        {
            if (typeKey.Equals("Hero", StringComparison.OrdinalIgnoreCase)) return new HeroSettings();
            if (typeKey.Equals("Text", StringComparison.OrdinalIgnoreCase)) return new TextSettings();
            if (typeKey.Equals("Gallery", StringComparison.OrdinalIgnoreCase)) return new GallerySettings();
            return null;
        }
    }

    private static string GetDefaultJsonByKey(string key)
    {
        if (key.Equals("Hero", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new HeroSettings { Heading = "Welcome", Subheading = "Your tagline here" });

        if (key.Equals("Text", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new TextSettings { Title = "Title", Body = "Your content here." });

        if (key.Equals("Gallery", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new GallerySettings { Layout = "grid", ImageAssetIds = new List<int>() });

        return "{}";
    }
    private IQueryable<Page> PagesForCurrentUser()
    {
        return _db.Pages
            .AsNoTracking()
            .Where(p => p.TenantId == _tenant.TenantId && !p.IsDeleted);
    }

    // =========================
    // Concurrency helpers
    // =========================
    private static byte[]? FromBase64(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return Convert.FromBase64String(s); } catch { return null; }
    }

    private void ApplyOptimisticConcurrency(PageRevision draft, string? base64RowVersion)
    {
        var token = FromBase64(base64RowVersion);
        if (token is null || token.Length == 0)
            throw new InvalidOperationException("Missing or invalid draft revision concurrency token.");

        _db.Entry(draft).Property(x => x.RowVersion).OriginalValue = token;
    }

    private static string ToBase64(byte[]? rv)
        => (rv is null || rv.Length == 0) ? "" : Convert.ToBase64String(rv);

    private JsonResult ConcurrencyConflict()
        => new(new { ok = false, error = "This draft was changed by someone else. Please reload." }) { StatusCode = 409 };
}
