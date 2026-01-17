using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Pages;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Pages;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;
using Page = WebsiteBuilder.Models.Page;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages
{
    public class EditModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IPagePublishingService _pagePublishingService;
        private readonly ITenantNavigationService _nav;
        private readonly PagePublishValidator _pagePublishValidator;
        private readonly ISectionValidationService _sectionValidation;
        private readonly IPageRevisionSectionService _pageRevisionSectionService;

        public EditModel(
            DataContext db,
            ITenantContext tenant,
            IPagePublishingService pagePublishingService,
            ITenantNavigationService nav,
            PagePublishValidator pagePublishValidator,
            ISectionValidationService sectionValidation,
            IPageRevisionSectionService pageRevisionSectionService)
        {
            _db = db;
            _tenant = tenant;
            _pagePublishingService = pagePublishingService;
            _nav = nav;
            _pagePublishValidator = pagePublishValidator;
            _sectionValidation = sectionValidation;
            _pageRevisionSectionService = pageRevisionSectionService;
        }
        public Page PageEntity { get; set; } = default!;

        [BindProperty]
        public PageEditVm Input { get; set; } = new();

        public SelectList PageStatusSelect { get; private set; } = default!;
        public bool SaveSuccess { get; private set; }
        public string TenantHost => _tenant.Host ?? string.Empty;

        // Sections (for drag-drop reorder UI)
        public List<SectionRowVm> Sections { get; set; } = new();

        // Trash (soft-deleted draft sections)
        public List<SectionRowVm> TrashedSections { get; set; } = new();

        // ===== Publish/Archive UI state =====
        public int SectionCount { get; private set; }
        public int TrashCount { get; private set; }
        public bool HasSections => SectionCount > 0;

        public bool HasSlug => !string.IsNullOrWhiteSpace(Input?.Slug);

        public bool IsPublished => Input.PageStatusId == PageStatusIds.Published;
        public bool IsDraft => Input.PageStatusId == PageStatusIds.Draft;
        public bool IsArchived => Input.PageStatusId == PageStatusIds.Archived;

        public bool CanPublish => !IsPublished && !IsArchived && HasSlug && HasSections;
        public bool CanUnpublish => IsPublished;
        public bool CanArchive => !IsArchived;
        public bool CanRestore => IsArchived;

        public List<SelectListItem> SectionTypeOptions { get; private set; } = new();

        public sealed record SectionTypeLookupVm(int Id, string Name, string Key);

        public IReadOnlyList<SectionTypeLookupVm> SectionTypeLookup { get; private set; }
            = Array.Empty<SectionTypeLookupVm>();

        public string GetSectionTitle(SectionRowVm s)
        {
            var st = SectionTypeLookup.FirstOrDefault(x => x.Id == s.SectionTypeId);
            return st?.Name ?? $"SectionType #{s.SectionTypeId}";
        }

        public string GetEditorPartialPath(SectionRowVm s)
        {
            var st = SectionTypeLookup.FirstOrDefault(x => x.Id == s.SectionTypeId);
            var key = (st?.Key ?? "").Trim().ToLowerInvariant();

            return key switch
            {
                "hero" => "~/Pages/Shared/Sections/Editors/_HeroEditor.cshtml",
                "text" => "~/Pages/Shared/Sections/Editors/_TextEditor.cshtml",
                "gallery" => "~/Pages/Shared/Sections/Editors/_GalleryEditor.cshtml",
                _ => "~/Pages/Shared/Sections/Editors/_UnknownEditor.cshtml"
            };
        }



        public string PreviewUrl
        {
            get
            {
                var slug = (Input?.Slug ?? string.Empty).Trim().Trim('/');
                if (string.IsNullOrWhiteSpace(slug))
                    slug = "home";

                return $"/{slug}?preview=true";
            }
        }

        // =======================
        // PAGE LOAD (GET)
        // =======================
        public async Task<IActionResult> OnGetAsync(int id, bool saveSuccess = false, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            PageEntity = page;

            // Map to VM (keep this minimal and safe)
            Input = new PageEditVm
            {
                Id = page.Id,
                Title = page.Title,
                Slug = page.Slug,
                PageStatusId = page.PageStatusId,
                ShowInNavigation = page.ShowInNavigation,
                NavigationOrder = page.NavigationOrder
            };

            SaveSuccess = saveSuccess;

            await BuildSelectListsAsync(Input.PageStatusId, ct);
            await LoadSectionTypeLookupAsync(ct);
            await LoadSectionTypeOptionsAsync(ct);
            await LoadSectionCountAsync(page.Id, ct);
            await LoadTrashCountAsync(page.Id, ct);
            await LoadSectionsAsync(page.Id, ct);
            await LoadTrashedSectionsAsync(page.Id, ct);

            return Page();
        }
        private Guid GetUserGuid()
        {
            // If identity exists later, this will start working automatically.
            var s = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(s, out var g) ? g : Guid.Empty;
        }

        // =======================
        // PAGE SAVE (POST)
        // =======================
        public async Task<IActionResult> OnPostAsync(CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            if (!ModelState.IsValid)
            {
                await BuildSelectListsAsync(Input.PageStatusId, ct);
                await LoadSectionTypeLookupAsync(ct);
                await LoadSectionTypeOptionsAsync(ct);
                await LoadSectionCountAsync(Input.Id, ct);
                await LoadTrashCountAsync(Input.Id, ct);
                await LoadSectionsAsync(Input.Id, ct);
                await LoadTrashedSectionsAsync(Input.Id, ct);
                return Page();
            }

            var page = await _db.Pages
                .FirstOrDefaultAsync(p => p.Id == Input.Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            // IMPORTANT: keep Slug sanitized and stable
            page.Title = (Input.Title ?? "").Trim();
            page.Slug = SanitizeSlug(Input.Slug, page.Title);

            // Do not allow manual status changes here (Publish/Unpublish/Archive handlers own status)
            // If you still have a status dropdown in _Form.cshtml, this enforces the server-side rule.

            page.ShowInNavigation = Input.ShowInNavigation;
            page.NavigationOrder = Input.NavigationOrder;

            var userGuid = GetUserIdOrEmpty();
            page.UpdatedAt = DateTime.UtcNow;
            page.UpdatedBy = userGuid;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();

            return RedirectToPage(new { id = page.Id, saveSuccess = true });
        }

        // =======================
        // PUBLISH / UNPUBLISH / ARCHIVE
        // =======================
        public async Task<IActionResult> OnPostPublishAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            // Guard: must have a draft revision with sections
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == id && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .FirstOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                TempData["Error"] = "Publish blocked. No draft revision found for this page.";
                return RedirectToPage(new { id });
            }

            var hasAnySections = await _db.PageRevisionSections
                .AsNoTracking()
                .AnyAsync(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    s.IsActive, ct);

            if (!hasAnySections)
            {
                TempData["Error"] = "Publish blocked. Add at least one section before publishing.";
                return RedirectToPage(new { id });
            }

            // Validate ALL sections before attempting publish
            var validation = await _pagePublishValidator.ValidateDraftSectionsAsync(id, ct);

            if (!validation.IsValid)
            {
                var message = string.Join(" ",
                    validation.Errors.SelectMany(e =>
                        e.Messages.Select(m => $"[Section #{e.SectionId} - {e.TypeKey}] {m}")
                    )
                );

                TempData["Error"] = "Publish blocked. Fix invalid sections first. " + message;
                return RedirectToPage(new { id });
            }

            var userGuid = GetUserIdOrEmpty();

            // Delegate publish transaction to the service
            var result = await _pagePublishingService.PublishAsync(id, userGuid, ct);

            if (!result.Success)
            {
                TempData["Error"] = string.Join(" ", result.Errors);
                return RedirectToPage(new { id });
            }

            _nav.Invalidate();
            TempData["Success"] = "Page published.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostUnpublishAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            if (page.PageStatusId != PageStatusIds.Published)
            {
                TempData["Error"] = "Unpublish is only available for published pages.";
                return RedirectToPage(new { id });
            }

            page.PageStatusId = PageStatusIds.Draft;

            var userGuid = GetUserIdOrEmpty();
            page.UpdatedBy = userGuid;
            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();

            TempData["Success"] = "Page moved back to Draft.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostArchiveAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            if (page.PageStatusId == PageStatusIds.Archived)
            {
                TempData["Success"] = "Page is already archived.";
                return RedirectToPage(new { id });
            }

            page.PageStatusId = PageStatusIds.Archived;
            page.ShowInNavigation = false;

            var userGuid = GetUserIdOrEmpty();
            page.UpdatedBy = userGuid;
            page.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _nav.Invalidate();

            TempData["Success"] = "Page archived.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            if (page.PageStatusId != PageStatusIds.Archived)
            {
                TempData["Error"] = "Restore is only available for archived pages.";
                return RedirectToPage(new { id });
            }

            var userGuid = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            page.PageStatusId = PageStatusIds.Draft;
            page.UpdatedBy = userGuid;
            page.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);

            // Optional: ensure a draft revision exists after restore (so section CRUD works immediately)
            await EnsureDraftRevisionExistsAsync(page, ct);

            _nav.Invalidate();

            TempData["Success"] = "Page restored to Draft.";
            return RedirectToPage(new { id });
        }


        // =======================
        // SECTIONS CRUD (DRAFT REVISION)
        // =======================
        public async Task<IActionResult> OnPostAddSectionAsync(int id, int sectionTypeId, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            if (id <= 0 || sectionTypeId <= 0)
                return RedirectToPage(new { id });

            var page = await _db.Pages
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

            if (page == null)
                return NotFound();

            if (!IsDraftEditable(page))
            {
                TempData["Error"] = "This page is not editable in its current status. Unpublish it to edit sections.";
                return RedirectToPage(new { id });
            }

            var sectionType = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.Id == sectionTypeId && st.IsActive && !st.IsDeleted)
                .Select(st => new { st.Id, st.Key })
                .FirstOrDefaultAsync(ct);

            if (sectionType == null)
            {
                TempData["Error"] = "Invalid section type.";
                return RedirectToPage(new { id });
            }

            var userGuid = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            // Create or reuse draft revision (recommended: make this concurrency-safe)
            var draftRevisionId = await EnsureDraftRevisionExistsAsync(page, ct);

            var typeKey = (sectionType.Key ?? "").Trim().ToLowerInvariant();
            var settingsJson = typeKey switch
            {
                "hero" => """{"headline":"Headline","subheadline":"Subheadline","ctaText":"Learn more","ctaUrl":"/"}""",
                "text" => """{"text":"Your text here..."}""",
                "gallery" => """{"items":[]}""",
                _ => "{}"
            };

            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable, ct);

                    try
                    {
                        var nextSort = await _db.PageRevisionSections
                            .AsNoTracking()
                            .Where(s =>
                                s.TenantId == _tenant.TenantId &&
                                s.PageRevisionId == draftRevisionId &&
                                // s.IsActive &&
                                !s.IsDeleted)
                            .Select(s => (int?)s.SortOrder)
                            .MaxAsync(ct) ?? 0;

                        var section = new PageRevisionSection
                        {
                            TenantId = _tenant.TenantId,
                            PageRevisionId = draftRevisionId,
                            SectionTypeId = sectionTypeId,
                            SortOrder = nextSort + 1,

                            IsActive = true,
                            IsDeleted = false,

                            SettingsJson = settingsJson,

                            CreatedAt = now,
                            CreatedBy = userGuid,
                            UpdatedAt = now,
                            UpdatedBy = userGuid
                        };

                        _db.PageRevisionSections.Add(section);
                        await _db.SaveChangesAsync(ct);

                        await tx.CommitAsync(ct);
                        return RedirectToPage(new { id });
                    }
                    catch (DbUpdateException ex) when (IsUniqueSortOrderViolation(ex))
                    {
                        await tx.RollbackAsync(ct);

                        if (attempt == 3)
                        {
                            TempData["Error"] =
                                "Unable to add section due to a concurrent update. Please try again.";
                            return RedirectToPage(new { id });
                        }
                    }
                }

                TempData["Error"] = "Unable to add section. Please try again.";
                return RedirectToPage(new { id });
            });
        }


        private static bool IsUniqueSortOrderViolation(DbUpdateException ex)
        {
            // SQL Server unique index violation = 2601 or 2627
            if (ex.InnerException is SqlException sql)
                return sql.Number == 2601 || sql.Number == 2627;

            return false;
        }

        public sealed class ReorderSectionsRequest
        {
            public int PageId { get; set; }
            public int[] OrderedSectionIds { get; set; } = Array.Empty<int>();
        }


        public async Task<IActionResult> OnPostReorderSectionsAsync(
            [FromBody] ReorderSectionsRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.PageId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid page id." });

            var orderedIds = request.OrderedSectionIds?
                .Where(x => x > 0)
                .Distinct()
                .ToArray() ?? Array.Empty<int>();

            if (orderedIds.Length == 0)
                return new JsonResult(new { ok = false, message = "No sections provided." });

            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == request.PageId &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return new JsonResult(new { ok = false, message = "Page not found." }) { StatusCode = 404 };

            if (page.PageStatusId != PageStatusIds.Draft)
                return new JsonResult(new { ok = false, message = "Page is not editable. Unpublish it to reorder sections." })
                { StatusCode = 409 };

            if (!page.DraftRevisionId.HasValue || page.DraftRevisionId.Value <= 0)
                return new JsonResult(new { ok = false, message = "Draft revision not found." }) { StatusCode = 409 };

            var draftRevisionId = page.DraftRevisionId.Value;

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, ct);

                try
                {
                    // IMPORTANT: load ALL reorderable sections (so we can validate the request is complete)
                    var all = await _db.PageRevisionSections
                        .AsTracking()
                        .Where(s =>
                            s.TenantId == _tenant.TenantId &&
                            s.PageRevisionId == draftRevisionId &&
                            !s.IsDeleted &&
                            s.IsActive) // keep this if your unique index is filtered by IsActive=1
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.Id)
                        .ToListAsync(ct);

                    if (all.Count == 0)
                    {
                        await tx.RollbackAsync(ct);
                        return new JsonResult(new { ok = false, message = "No sections to reorder." }) { StatusCode = 409 };
                    }

                    // Require the client to send the FULL set of IDs
                    if (orderedIds.Length != all.Count)
                    {
                        await tx.RollbackAsync(ct);
                        return new JsonResult(new { ok = false, message = "Reorder list is incomplete. Refresh the page and try again." })
                        { StatusCode = 409 };
                    }

                    var allIds = all.Select(x => x.Id).OrderBy(x => x).ToArray();
                    var reqIds = orderedIds.OrderBy(x => x).ToArray();

                    if (!allIds.SequenceEqual(reqIds))
                    {
                        await tx.RollbackAsync(ct);
                        return new JsonResult(new { ok = false, message = "One or more sections do not belong to the current draft. Refresh and try again." })
                        { StatusCode = 409 };
                    }

                    var orderMap = orderedIds
                        .Select((id, idx) => new { id, sort = idx + 1 })
                        .ToDictionary(x => x.id, x => x.sort);

                    var userGuid = GetUserIdOrEmpty();
                    var now = DateTime.UtcNow;

                    // Two-phase update to avoid unique-index collisions during swaps
                    // Phase A: assign temporary unique sort orders far away from normal range
                    var tempBase = 1000000;
                    foreach (var s in all)
                    {
                        s.SortOrder = tempBase + orderMap[s.Id];
                        s.UpdatedAt = now;
                        s.UpdatedBy = userGuid;
                    }

                    await _db.SaveChangesAsync(ct);

                    // Phase B: assign final 1..N
                    foreach (var s in all)
                    {
                        s.SortOrder = orderMap[s.Id];
                        s.UpdatedAt = now;
                        s.UpdatedBy = userGuid;
                    }

                    await _db.SaveChangesAsync(ct);

                    // Optional normalization (now safe)
                    await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, draftRevisionId, ct);

                    await tx.CommitAsync(ct);
                    return new JsonResult(new { ok = true });
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            });
        }



        public async Task<IActionResult> OnPostSaveSectionAsync(
            [FromBody] SaveSectionRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.SectionId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid section id." });

            var section = await _db.PageRevisionSections
                .AsTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == request.SectionId &&
                    s.TenantId == _tenant.TenantId &&
                    !s.IsDeleted, ct);

            if (section == null)
                return new JsonResult(new { ok = false, message = "Section not found." });

            // Ensure this section belongs to a current draft revision of a page in this tenant
            var isCurrentDraft = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.DraftRevisionId == section.PageRevisionId &&
                    !p.IsDeleted, ct);

            if (!isCurrentDraft)
                return new JsonResult(new { ok = false, message = "Section is not part of the current draft." });

            var typeKey = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.Id == section.SectionTypeId && st.IsActive && !st.IsDeleted)
                .Select(st => st.Key)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(typeKey))
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = "Section type key not found. Ensure SectionTypes.Key is populated and the type is active.",
                    sectionId = section.Id,
                    sectionTypeId = section.SectionTypeId
                });
            }

            typeKey = typeKey.Trim().ToLowerInvariant();

            var json = string.IsNullOrWhiteSpace(request.SettingsJson)
                ? "{}"
                : request.SettingsJson.Trim();

            var validation = await _sectionValidation.ValidateAsync(typeKey, json);

            if (!validation.IsValid)
            {
                return new JsonResult(new
                {
                    ok = false,
                    message = "Validation failed.",
                    errors = validation.Errors
                });
            }

            section.SettingsJson = json;

            var userGuid = GetUserIdOrEmpty();
            section.UpdatedBy = userGuid;
            section.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new JsonResult(new { ok = true });
        }

        // =======================
        // LOAD HELPERS
        // =======================
        private async Task LoadSectionCountAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                SectionCount = 0;
                return;
            }

            SectionCount = await _db.PageRevisionSections
                .AsNoTracking()
                .CountAsync(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    s.IsActive, ct);
        }

        private async Task LoadSectionsAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                Sections = new List<SectionRowVm>();
                return;
            }

            Sections = await _db.PageRevisionSections
                .AsNoTracking()
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    !s.IsDeleted &&
                    s.IsActive)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .Select(s => new SectionRowVm
                {
                    Id = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    SortOrder = s.SortOrder,
                    SettingsJson = s.SettingsJson
                })
                .ToListAsync(ct);
        }

        private async Task LoadSectionTypeOptionsAsync(CancellationToken ct)
        {
            SectionTypeOptions = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.IsActive && !st.IsDeleted)
                .OrderBy(st => st.SortOrder)
                .ThenBy(st => st.Name)
                .Select(st => new SelectListItem
                {
                    Value = st.Id.ToString(),
                    Text = st.Name
                })
                .ToListAsync(ct);
        }

        private async Task LoadSectionTypeLookupAsync(CancellationToken ct)
        {
            SectionTypeLookup = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => st.IsActive && !st.IsDeleted)
                .OrderBy(st => st.SortOrder)
                .ThenBy(st => st.Name)
                .Select(st => new SectionTypeLookupVm(st.Id, st.Name, st.Key))
                .ToListAsync(ct);
        }

        private async Task BuildSelectListsAsync(int selectedPageStatusId, CancellationToken ct)
        {
            var statuses = await _db.PageStatuses
                .AsNoTracking()
                .Where(s => s.IsActive && !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name)
                .ToListAsync(ct);

            PageStatusSelect = new SelectList(statuses, "Id", "Name", selectedPageStatusId);
            ViewData["PageStatuses"] = PageStatusSelect;
        }

        private static string SanitizeSlug(string? slug, string? title)
        {
            var value = (slug ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = (title ?? string.Empty).Trim();

            value = value.Trim().Trim('/');
            value = value.Replace(' ', '-');

            while (value.Contains("--"))
                value = value.Replace("--", "-");

            return value.ToLowerInvariant();
        }

        private Guid GetUserIdOrEmpty()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userId, out var g) ? g : Guid.Empty;
        }

        private async Task<int> EnsureDraftRevisionExistsAsync(Page page, CancellationToken ct)
        {
            var userGuid = GetUserGuid();
            var now = DateTime.UtcNow;

            if (page.DraftRevisionId.HasValue && page.DraftRevisionId.Value > 0)
                return page.DraftRevisionId.Value;

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, ct);

                var fresh = await _db.Pages
                    .FirstOrDefaultAsync(p => p.Id == page.Id && p.TenantId == _tenant.TenantId && !p.IsDeleted, ct);

                if (fresh == null)
                    throw new InvalidOperationException("Page not found.");

                if (fresh.DraftRevisionId.HasValue && fresh.DraftRevisionId.Value > 0)
                {
                    await tx.CommitAsync(ct);
                    return fresh.DraftRevisionId.Value;
                }

                var rev = new PageRevision
                {
                    TenantId = _tenant.TenantId,
                    PageId = page.Id,
                    CreatedAt = now,
                    CreatedBy = userGuid,
                    UpdatedAt = now,
                    UpdatedBy = userGuid,
                    IsActive = true,
                    IsDeleted = false
                };

                _db.PageRevisions.Add(rev);
                await _db.SaveChangesAsync(ct);

                fresh.DraftRevisionId = rev.Id;
                fresh.UpdatedAt = now;
                fresh.UpdatedBy = userGuid;

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return rev.Id;
            });
        }



        public sealed class SaveSectionRequest
        {
            public int SectionId { get; set; }
            public string? SettingsJson { get; set; }
        }

        public sealed class SectionIdRequest
        {
            public int SectionId { get; set; }
        }

        public async Task<IActionResult> OnPostDeleteRevisionSectionAsync(
            [FromBody] SectionIdRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request == null || request.SectionId <= 0)
                return new JsonResult(new { ok = false, message = "Invalid section id." });

            var section = await _db.PageRevisionSections
                .AsTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == request.SectionId &&
                    s.TenantId == _tenant.TenantId, ct);

            if (section == null)
                return new JsonResult(new { ok = false, message = "Section not found." });

            // Find the owning page of this draft revision
            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.DraftRevisionId == section.PageRevisionId, ct);

            if (page == null)
                return new JsonResult(new { ok = false, message = "Section is not part of the current draft." });

            // ✅ Status guard: allow draft edits only
            if (page.PageStatusId != PageStatusIds.Draft)
                return new JsonResult(new { ok = false, message = "Page is not editable. Unpublish it to modify sections." })
                { StatusCode = 409 };

            if (section.IsDeleted)
                return new JsonResult(new { ok = true }); // idempotent

            var userGuid = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            section.IsDeleted = true;
            section.DeletedAt = now;
            section.DeletedBy = userGuid;
            section.UpdatedAt = now;
            section.UpdatedBy = userGuid;

            await _db.SaveChangesAsync(ct);

            // ✅ Compact SortOrder after delete (keeps 1..N, removes gaps)
            await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, section.PageRevisionId, ct);

            return new JsonResult(new { ok = true });
        }

        public async Task<IActionResult> OnPostRestoreRevisionSectionsAsync(
            [FromBody] SectionIdsRequest request,
            CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { ok = false, message = "Tenant not resolved." });

            if (request?.SectionIds == null || request.SectionIds.Length == 0)
                return new JsonResult(new { ok = false, message = "No sections selected." });

            var ids = request.SectionIds.Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
                return new JsonResult(new { ok = false, message = "No valid section ids." });

            // Load the sections (trashed only) for this tenant
            var sections = await _db.PageRevisionSections
                .AsTracking()
                .Where(s => s.TenantId == _tenant.TenantId && ids.Contains(s.Id))
                .ToListAsync(ct);

            if (sections.Count == 0)
                return new JsonResult(new { ok = false, message = "No matching sections found." });

            // All must belong to the same draft revision for safety
            var revisionId = sections.Select(s => s.PageRevisionId).Distinct().ToArray();
            if (revisionId.Length != 1)
                return new JsonResult(new { ok = false, message = "Selected sections must belong to the same draft." })
                { StatusCode = 409 };

            var draftRevisionId = revisionId[0];

            // Validate the draft revision belongs to current draft page and is Draft status
            var page = await _db.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.DraftRevisionId == draftRevisionId, ct);

            if (page == null)
                return new JsonResult(new { ok = false, message = "Draft not found." }) { StatusCode = 409 };

            if (page.PageStatusId != PageStatusIds.Draft)
                return new JsonResult(new { ok = false, message = "Page is not editable. Unpublish it to modify sections." })
                { StatusCode = 409 };

            // Append restored sections to end
            var maxSort = await _db.PageRevisionSections
                .Where(s => s.TenantId == _tenant.TenantId && s.PageRevisionId == draftRevisionId && !s.IsDeleted)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync(ct) ?? 0;

            var userGuid = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            var restoredCount = 0;

            foreach (var s in sections.Where(x => x.IsDeleted).OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            {
                maxSort++;
                s.IsDeleted = false;
                s.DeletedAt = null;
                s.DeletedBy = null;
                s.SortOrder = maxSort;
                s.UpdatedAt = now;
                s.UpdatedBy = userGuid;
                restoredCount++;
            }

            await _db.SaveChangesAsync(ct);

            await _pageRevisionSectionService.CompactSortOrderAsync(_tenant.TenantId, draftRevisionId, ct);

            return new JsonResult(new { ok = true, restored = restoredCount });
        }



        private async Task LoadTrashCountAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                TrashCount = 0;
                return;
            }

            TrashCount = await _db.PageRevisionSections
                .AsNoTracking()
                .CountAsync(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    s.IsDeleted, ct);
        }

        private async Task LoadTrashedSectionsAsync(int pageId, CancellationToken ct)
        {
            var draftRevisionId = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && p.Id == pageId && !p.IsDeleted)
                .Select(p => p.DraftRevisionId)
                .SingleOrDefaultAsync(ct);

            if (draftRevisionId == null)
            {
                TrashedSections = new List<SectionRowVm>();
                return;
            }

            TrashedSections = await _db.PageRevisionSections
                .AsNoTracking()
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId &&
                    s.IsDeleted)
                .OrderByDescending(s => s.DeletedAt)
                .ThenByDescending(s => s.Id)
                .Select(s => new SectionRowVm
                {
                    Id = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    SortOrder = s.SortOrder,
                    SettingsJson = s.SettingsJson
                })
                .ToListAsync(ct);
        }

        public sealed record SectionRowRenderVm(
            SectionRowVm Section,
            string Title,
            string EditorPartialPath);

        public SectionRowRenderVm BuildSectionRowRenderVm(SectionRowVm s)
        {
            return new SectionRowRenderVm(
                s,
                GetSectionTitle(s),
                GetEditorPartialPath(s));
        }
        public async Task<IActionResult> OnGetRevisionSectionRowAsync(int sectionId, CancellationToken ct = default)
        {
            if (!_tenant.IsResolved)
                return Content("Tenant not resolved.", "text/plain");

            if (sectionId <= 0)
                return Content("Invalid section id.", "text/plain");

            var section = await _db.PageRevisionSections
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.Id == sectionId &&
                    s.TenantId == _tenant.TenantId &&
                    !s.IsDeleted &&
                    s.IsActive, ct);

            if (section == null)
                return Content("Section not found.", "text/plain");

            var isCurrentDraft = await _db.Pages
                .AsNoTracking()
                .AnyAsync(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.DraftRevisionId == section.PageRevisionId, ct);

            if (!isCurrentDraft)
                return Content("Section is not part of the current draft.", "text/plain");

            await LoadSectionTypeLookupAsync(ct);

            var row = new SectionRowVm
            {
                Id = section.Id,
                SectionTypeId = section.SectionTypeId,
                SortOrder = section.SortOrder,
                SettingsJson = section.SettingsJson
            };

            var vm = BuildSectionRowRenderVm(row);

            var vd = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<SectionRowRenderVm>(
                metadataProvider: MetadataProvider,
                modelState: ModelState)
            {
                Model = vm
            };

            return new PartialViewResult
            {
                ViewName = "~/Pages/Admin/Pages/Partials/_PageRevisionSectionRow.cshtml",
                ViewData = vd
            };
        }

        private bool IsDraftEditable(Page page)
        {
            // Draft only. Published/Archived should be changed via Unpublish/Publish actions.
            return page.PageStatusId == PageStatusIds.Draft;
        }

    }
    public sealed class SectionIdsRequest
    {
        public int[] SectionIds { get; set; } = Array.Empty<int>();
    }

}
