using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Navigation;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages.Admin.Navigation
{
    public class EditModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ITenantNavigationService _nav;

        public EditModel(DataContext db, ITenantContext tenant, ITenantNavigationService nav)
        {
            _db = db;
            _tenant = tenant;
            _nav = nav;
        }

        [BindProperty(SupportsGet = true)]
        public int MenuId { get; set; }

        public List<NavNodeVm> Tree { get; set; } = new();

        // Used by JS to render page dropdowns for newly added nodes (Published ONLY)
        public string PagesJson { get; set; } = "[]";

        // ✅ Used by JS to show warning badges about the selected Page
        public string PageFlagsJson { get; set; } = "{}";

        // ✅ Declaration you asked for (the “flags” object)
        public sealed record PageNavFlags(
            bool ShowInNavigation,
            int PageStatusId,
            bool IsActive,
            bool IsDeleted
        );
        public string PublicBaseUrl { get; private set; } = "/";

        public Dictionary<int, PageNavFlags> PageFlagsById { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(int menuId)
        {
            MenuId = menuId;

            // ✅ Public site base URL (used by “Save & View Site”)
            var primaryHost = await _db.DomainMappings
                .AsNoTracking()
                .Where(d => d.TenantId == _tenant.TenantId && d.IsPrimary)
                .Select(d => d.Host)
                .FirstOrDefaultAsync();

            var scheme = Request.Scheme;                 // http/https
            var host = string.IsNullOrWhiteSpace(primaryHost)
                ? Request.Host.Value                     // fallback to current host
                : primaryHost.Trim();

            var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : ""; // if you use PathBase for tenancy

            PublicBaseUrl = $"{scheme}://{host}{pathBase}/";


            var items = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x => x.TenantId == _tenant.TenantId && x.MenuId == MenuId && !x.IsDeleted)
                .OrderBy(x => x.ParentId)
                .ThenBy(x => x.SortOrder)
                .ToListAsync();

            var publishedPages = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published)
                .OrderBy(p => p.Title)
                .Select(p => new PageOptionVm
                {
                    Id = p.Id,
                    Title = p.Title,
                    Slug = p.Slug
                })
                .ToListAsync();

            // ------------------------------------------------------------
            // ✅ Page visibility flags (for Navigation Editor UI warnings)
            // ------------------------------------------------------------
            PageFlagsById = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId)
                .Select(p => new
                {
                    p.Id,
                    p.ShowInNavigation,
                    p.PageStatusId,
                    p.IsActive,
                    p.IsDeleted
                })
                .ToDictionaryAsync(
                    x => x.Id,
                    x => new PageNavFlags(
                        x.ShowInNavigation,
                        x.PageStatusId,
                        x.IsActive,
                        x.IsDeleted
                    )
                );

            PagesJson = JsonSerializer.Serialize(
                publishedPages,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );

            // ✅ Serialize flags for client-side warning badges
            PageFlagsJson = JsonSerializer.Serialize(
                PageFlagsById,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );


            // Include referenced pages even if not published, so existing items remain selectable
            var referencedPageIds = items
                .Where(x => x.PageId.HasValue && x.PageId.Value > 0)
                .Select(x => x.PageId!.Value)
                .Distinct()
                .ToList();

            var publishedIdSet = publishedPages.Select(x => x.Id).ToHashSet();

            var missingIds = referencedPageIds
                .Where(id => !publishedIdSet.Contains(id))
                .Distinct()
                .ToList();

            List<PageOptionVm> extraNotPublished = new();

            if (missingIds.Count > 0)
            {
                extraNotPublished = await _db.Pages
                    .AsNoTracking()
                    .Where(p =>
                        p.TenantId == _tenant.TenantId &&
                        missingIds.Contains(p.Id))
                    .OrderBy(p => p.Title)
                    .Select(p => new PageOptionVm
                    {
                        Id = p.Id,
                        Title =
                            p.Title +
                            (p.IsDeleted ? " (Deleted)" :
                             p.PageStatusId != PageStatusIds.Published ? " (Not published)" :
                             ""),
                        Slug = p.Slug
                    })
                    .ToListAsync();
            }

            var pageOptionsForExistingNodes = extraNotPublished
                .Concat(publishedPages)
                .ToList();

            Tree = BuildTree(items, pageOptionsForExistingNodes);
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync([FromBody] NavSaveRequestVm request)
        {
            if (request == null)
                return BadRequest(new { success = false, error = "Invalid payload." });

            if (request.MenuId <= 0)
                return BadRequest(new { success = false, error = "MenuId is required." });

            if (request.Items == null)
                return BadRequest(new { success = false, error = "Items is required." });

            if (!_tenant.IsResolved)
                return BadRequest(new { success = false, error = "Tenant is not resolved." });

            var userId = GetUserIdOrEmptyGuid();
            var now = DateTime.UtcNow;

            // Load all rows for this tenant/menu (so we can update/delete)
            var existing = await _db.NavigationMenuItems
                .Where(x => x.TenantId == _tenant.TenantId && x.MenuId == request.MenuId)
                .ToListAsync();

            var existingById = existing.ToDictionary(x => x.Id);

            // -----------------------------
            // Basic validation
            // -----------------------------
            foreach (var i in request.Items.Where(x => !x.IsDeleted))
            {
                if (string.IsNullOrWhiteSpace(i.Label))
                    return BadRequest(new { success = false, error = "All non-deleted items must have a label." });

                if ((i.Label ?? "").Length > 200)
                    return BadRequest(new { success = false, error = "Label exceeds 200 chars." });

                if (!string.IsNullOrWhiteSpace(i.Url) && i.Url.Length > 500)
                    return BadRequest(new { success = false, error = "Url exceeds 500 chars." });
            }

            // ✅ Validate PageIds ONLY for internal links
            var internalPageIds = request.Items
                .Where(x => !x.IsDeleted)
                .Where(x => string.Equals(x.LinkType, "page", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.PageId.HasValue && x.PageId.Value > 0)
                .Select(x => x.PageId!.Value)
                .Distinct()
                .ToList();

            if (internalPageIds.Count > 0)
            {
                var allowedPublishedIds = await _db.Pages
                    .AsNoTracking()
                    .Where(p =>
                        p.TenantId == _tenant.TenantId &&
                        !p.IsDeleted &&
                        p.PageStatusId == PageStatusIds.Published &&
                        internalPageIds.Contains(p.Id))
                    .Select(p => p.Id)
                    .ToListAsync();

                var allowedSet = allowedPublishedIds.ToHashSet();
                var invalidIds = internalPageIds.Where(id => !allowedSet.Contains(id)).ToList();

                if (invalidIds.Count > 0)
                {
                    var badItems = request.Items
                        .Where(x => !x.IsDeleted)
                        .Where(x => string.Equals(x.LinkType, "page", StringComparison.OrdinalIgnoreCase))
                        .Where(x => x.PageId.HasValue && invalidIds.Contains(x.PageId.Value))
                        .Select(x => x.Label ?? "Unnamed")
                        .Distinct()
                        .ToList();

                    var msg = badItems.Count > 0
                        ? $"These items link to a non-published page: {string.Join(", ", badItems)}. Publish the page first or switch to External URL."
                        : "One or more items link to a non-published page. Publish the page first or switch to External URL.";

                    return BadRequest(new { success = false, error = msg });
                }
            }

            // -----------------------------
            // Create new (temp id <= 0) items in a batch
            // -----------------------------
            var idMap = new Dictionary<int, int>(); // tempId -> realId
            var createdTemp = new Dictionary<int, NavigationMenuItem>();

            var newVms = request.Items
                .Where(x => x.Id <= 0 && !x.IsDeleted)
                .ToList();

            if (newVms.Count > 0)
            {
                foreach (var vm in newVms)
                {
                    var normalized = NormalizeLink(vm);

                    var entity = new NavigationMenuItem
                    {
                        TenantId = _tenant.TenantId,
                        MenuId = request.MenuId,
                        ParentId = null, // fixed in 2nd pass

                        SortOrder = vm.SortOrder,
                        Label = (vm.Label ?? string.Empty).Trim(),

                        PageId = normalized.PageId,
                        Url = normalized.Url,

                        OpenInNewTab = vm.OpenInNewTab,
                        IsActive = vm.IsActive,
                        IsPublished = vm.IsPublished,
                        AllowedRolesCsv = NormalizeRolesCsv(vm.AllowedRolesCsv),

                        IsDeleted = false,
                        CreatedAt = now,
                        CreatedBy = userId
                    };

                    _db.NavigationMenuItems.Add(entity);
                    createdTemp[vm.Id] = entity;
                }

                await _db.SaveChangesAsync();

                foreach (var kvp in createdTemp)
                    idMap[kvp.Key] = kvp.Value.Id;
            }

            int? ResolveParent(int? parentId)
            {
                if (!parentId.HasValue) return null;

                if (parentId.Value <= 0)
                    return idMap.TryGetValue(parentId.Value, out var real) ? real : (int?)null;

                return parentId.Value;
            }

            // -----------------------------
            // Apply updates (including deletes/restores) for all items
            // -----------------------------
            foreach (var vm in request.Items)
            {
                NavigationMenuItem entity;

                if (vm.Id <= 0)
                {
                    if (!createdTemp.TryGetValue(vm.Id, out entity!))
                        continue;
                }
                else
                {
                    if (!existingById.TryGetValue(vm.Id, out entity!))
                        continue;
                }

                ApplyVm(entity, vm, ResolveParent(vm.ParentId), userId, now);
            }

            await _db.SaveChangesAsync();

            // ✅ cache invalidation only after successful commit
            _nav.Invalidate(request.MenuId);

            return new JsonResult(new { success = true, idMap });
        }


        // -----------------------------
        // Helpers
        // -----------------------------
        private static string? NormalizeRolesCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;

            var parts = csv
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parts.Count == 0 ? null : string.Join(",", parts);
        }

        private static (int? PageId, string Url) NormalizeLink(NavSaveItemVm vm)
        {
            var linkType = (vm.LinkType ?? "page").Trim().ToLowerInvariant();

            if (linkType == "url")
            {
                var url = (vm.Url ?? string.Empty).Trim();
                return (null, url); // Url is used
            }

            // default: page
            var pageId = (vm.PageId.HasValue && vm.PageId.Value > 0) ? vm.PageId : null;
            return (pageId, string.Empty); // Url must be cleared when internal page
        }

        private void ApplyVm(
            NavigationMenuItem entity,
            NavSaveItemVm vm,
            int? resolvedParentId,
            Guid userId,
            DateTime nowUtc)
        {
            // -----------------------------
            // Structure (keep even when deleted)
            // -----------------------------
            entity.ParentId = resolvedParentId;
            entity.SortOrder = vm.SortOrder;

            // -----------------------------
            // Core fields (always)
            // -----------------------------
            entity.Label = (vm.Label ?? string.Empty).Trim();
            entity.OpenInNewTab = vm.OpenInNewTab;
            entity.IsPublished = vm.IsPublished;
            entity.IsActive = vm.IsActive;
            entity.AllowedRolesCsv = NormalizeRolesCsv(vm.AllowedRolesCsv);

            // -----------------------------
            // Link normalization
            // -----------------------------
            var normalized = NormalizeLink(vm);

            // PageId can be null; Url must never be null in your entity
            entity.PageId = normalized.PageId;
            entity.Url = normalized.Url ?? string.Empty;

            // -----------------------------
            // Soft delete + restore support
            // Rule:
            // - IsDeleted=true => delete (and set audit once)
            // - IsDeleted=false && Restore=true => restore + clear delete audit
            // - else => keep IsDeleted as-is (prevents accidental undelete)
            // -----------------------------
            if (vm.IsDeleted)
            {
                // If client mistakenly sends Restore=true too, deletion wins.
                // (No need to store Restore on entity; just ignore it.)

                if (!entity.IsDeleted)
                {
                    entity.IsDeleted = true;
                    entity.DeletedAt = nowUtc;
                    entity.DeletedBy = userId;
                }
                // If already deleted, keep existing DeletedAt/By (don’t rewrite history)
            }
            else if (vm.Restore)
            {
                // Explicit restore intent only
                if (entity.IsDeleted)
                {
                    entity.IsDeleted = false;
                    entity.DeletedAt = null;
                    entity.DeletedBy = null;
                }
                // If it wasn't deleted, restore is a no-op.
            }
            // else: do nothing (keeps current IsDeleted state)

            // -----------------------------
            // Audit (always)
            // -----------------------------
            entity.UpdatedAt = nowUtc;
            entity.UpdatedBy = userId;
        }


        private Guid GetUserIdOrEmptyGuid()
        {
            var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }


        public async Task<IActionResult> OnGetPageFlagsAsync(int menuId)
        {
            if (menuId <= 0) return new JsonResult(new { success = false, error = "Invalid menuId." });

            // Tenant-safe
            var flags = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId)
                .Select(p => new
                {
                    p.Id,
                    p.ShowInNavigation,
                    p.PageStatusId,
                    p.IsActive,
                    p.IsDeleted
                })
                .ToDictionaryAsync(
                    x => x.Id,
                    x => new PageNavFlags(
                        x.ShowInNavigation,
                        x.PageStatusId,
                        x.IsActive,
                        x.IsDeleted
                    )
                );

            return new JsonResult(new
            {
                success = true,
                flags
            });
        }
        public async Task<IActionResult> OnGetPageOptionsAsync(int menuId)
        {
            if (menuId <= 0) return new JsonResult(new { success = false, error = "Invalid menuId." });

            // 1) Published pages (normal list)
            var published = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.PageStatusId == PageStatusIds.Published)
                .OrderBy(p => p.Title)
                .Select(p => new PageOptionVm
                {
                    Id = p.Id,
                    Title = p.Title,
                    Slug = p.Slug
                })
                .ToListAsync();

            // 2) Include referenced pages (even if not published or deleted) so existing selections stay visible
            var referencedIds = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x => x.TenantId == _tenant.TenantId && x.MenuId == menuId && !x.IsDeleted)
                .Where(x => x.PageId.HasValue && x.PageId.Value > 0)
                .Select(x => x.PageId!.Value)
                .Distinct()
                .ToListAsync();

            var publishedSet = published.Select(x => x.Id).ToHashSet();
            var missingIds = referencedIds.Where(id => !publishedSet.Contains(id)).ToList();

            List<PageOptionVm> extras = new();

            if (missingIds.Count > 0)
            {
                extras = await _db.Pages
                    .AsNoTracking()
                    .Where(p =>
                        p.TenantId == _tenant.TenantId &&
                        missingIds.Contains(p.Id))
                    .OrderBy(p => p.Title)
                    .Select(p => new PageOptionVm
                    {
                        Id = p.Id,
                        Title =
                            p.Title +
                            (p.IsDeleted ? " (Deleted)" :
                             p.PageStatusId != PageStatusIds.Published ? " (Not published)" :
                             ""),
                        Slug = p.Slug
                    })
                    .ToListAsync();
            }

            var options = extras.Concat(published).ToList();

            return new JsonResult(new { success = true, pages = options });
        }



        private List<NavNodeVm> BuildTree(List<NavigationMenuItem> items, List<PageOptionVm> pages)
        {
            var byParent = items.ToLookup(x => x.ParentId);

            List<NavNodeVm> build(int? parentId)
            {
                return byParent[parentId]
                    .OrderBy(x => x.SortOrder)
                    .Select(x => new NavNodeVm
                    {
                        Id = x.Id,
                        ParentId = x.ParentId,
                        SortOrder = x.SortOrder,
                        Label = x.Label,

                        PageId = x.PageId,
                        Url = x.Url,

                        OpenInNewTab = x.OpenInNewTab,
                        IsActive = x.IsActive,
                        IsPublished = x.IsPublished,
                        AllowedRolesCsv = x.AllowedRolesCsv,

                        PageOptions = pages,
                        Children = build(x.Id)
                    })
                    .ToList();
            }

            return build(parentId: null);
        }
    }
}
