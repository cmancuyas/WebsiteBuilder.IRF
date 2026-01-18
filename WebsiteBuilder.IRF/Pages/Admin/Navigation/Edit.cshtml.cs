using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using System.Text.Json;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.ViewModels.Admin.Navigation;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Navigation
{
    public class EditModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IMemoryCache _cache;

        public EditModel(DataContext db, ITenantContext tenant, IMemoryCache cache)
        {
            _db = db;
            _tenant = tenant;
            _cache = cache;
        }

        [BindProperty(SupportsGet = true)]
        public int MenuId { get; set; }

        public List<NavNodeVm> Tree { get; set; } = new();

        // Used by JS to render page dropdowns for newly added nodes
        public string PagesJson { get; set; } = "[]";

        public async Task<IActionResult> OnGetAsync(int menuId)
        {
            MenuId = menuId;

            var pages = await _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId && !p.IsDeleted)
                .OrderBy(p => p.Title)
                .Select(p => new PageOptionVm { Id = p.Id, Title = p.Title, Slug = p.Slug })
                .ToListAsync();

            PagesJson = JsonSerializer.Serialize(pages);

            var items = await _db.NavigationMenuItems
                .AsNoTracking()
                .Where(x => x.TenantId == _tenant.TenantId && x.MenuId == MenuId && !x.IsDeleted)
                .OrderBy(x => x.ParentId)
                .ThenBy(x => x.SortOrder)
                .ToListAsync();

            Tree = BuildTree(items, pages);
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync([FromBody] NavSaveRequest request)
        {
            if (request == null) return BadRequest(new { success = false, error = "Invalid payload." });
            if (request.MenuId != MenuId && MenuId != 0) { /* no-op, MenuId comes from route */ }

            // server-side guardrails
            if (request.MenuId <= 0)
                return BadRequest(new { success = false, error = "MenuId is required." });

            // Items may reference temp negative IDs as ParentId.
            // We'll create real rows first and produce an ID map.
            var userId = GetUserIdOrEmpty();
            var now = DateTime.UtcNow;

            // Load existing
            var existing = await _db.NavigationMenuItems
                .Where(x => x.TenantId == _tenant.TenantId && x.MenuId == request.MenuId)
                .ToListAsync();

            var existingById = existing.ToDictionary(x => x.Id);

            // Validate labels
            foreach (var i in request.Items.Where(x => !x.IsDeleted))
            {
                if (string.IsNullOrWhiteSpace(i.Label))
                    return BadRequest(new { success = false, error = "All non-deleted items must have a label." });

                if (i.Label.Length > 200)
                    return BadRequest(new { success = false, error = "Label exceeds 200 chars." });

                if (!string.IsNullOrWhiteSpace(i.Url) && i.Url.Length > 500)
                    return BadRequest(new { success = false, error = "Url exceeds 500 chars." });
            }

            // 1) Create new items first (for temp IDs), without ParentId resolution yet.
            // We'll assign ParentId after we have the full temp->real map.
            var idMap = new Dictionary<int, int>(); // tempId -> realId

            foreach (var dto in request.Items.Where(x => x.Id <= 0 && !x.IsDeleted))
            {
                var entity = new NavigationMenuItem
                {
                    TenantId = _tenant.TenantId,
                    MenuId = request.MenuId,

                    // temporary, resolved later
                    ParentId = null,
                    SortOrder = dto.SortOrder,

                    Label = dto.Label.Trim(),
                    PageId = dto.PageId,
                    Url = dto.PageId.HasValue ? string.Empty : (dto.Url ?? string.Empty).Trim(),
                    OpenInNewTab = dto.OpenInNewTab,

                    IsActive = dto.IsActive,
                    IsDeleted = false,

                    CreatedAt = now,
                    CreatedBy = userId
                };

                _db.NavigationMenuItems.Add(entity);
                await _db.SaveChangesAsync(); // to get Id
                idMap[dto.Id] = entity.Id;
            }

            // Helper to resolve parent IDs that may be temp
            int? ResolveParent(int? parentId)
            {
                if (!parentId.HasValue) return null;
                if (parentId.Value <= 0)
                {
                    if (idMap.TryGetValue(parentId.Value, out var real))
                        return real;

                    // parent might have been deleted or missing
                    return null;
                }
                return parentId.Value;
            }

            // 2) Update existing + newly created entities with final ParentId/sort + fields
            foreach (var dto in request.Items)
            {
                if (dto.Id <= 0)
                {
                    if (!idMap.TryGetValue(dto.Id, out var realId))
                        continue;

                    var created = await _db.NavigationMenuItems
                        .FirstAsync(x => x.TenantId == _tenant.TenantId && x.Id == realId);

                    ApplyDto(created, dto, ResolveParent(dto.ParentId), userId, now);
                    continue;
                }

                if (!existingById.TryGetValue(dto.Id, out var entity))
                {
                    // ignore unknown IDs (tenant isolation / stale client)
                    continue;
                }

                ApplyDto(entity, dto, ResolveParent(dto.ParentId), userId, now);
            }

            await _db.SaveChangesAsync();

            // 3) Cache invalidation (public navigation)
            InvalidateNavCache(_tenant.TenantId, request.MenuId);

            return new JsonResult(new
            {
                success = true,
                idMap // allows client to swap temp IDs for real IDs
            });
        }

        private void ApplyDto(NavigationMenuItem entity, NavSaveItemDto dto, int? resolvedParentId, Guid userId, DateTime now)
        {
            entity.ParentId = resolvedParentId;
            entity.SortOrder = dto.SortOrder;

            entity.Label = (dto.Label ?? string.Empty).Trim();

            entity.PageId = dto.PageId;
            entity.Url = dto.PageId.HasValue ? string.Empty : (dto.Url ?? string.Empty).Trim();

            entity.OpenInNewTab = dto.OpenInNewTab;

            entity.IsActive = dto.IsActive;

            if (dto.IsDeleted)
            {
                entity.IsDeleted = true;
                entity.DeletedAt = now;
                entity.DeletedBy = userId;
            }
            else
            {
                // do not auto-undelete without explicit UX
                entity.UpdatedAt = now;
                entity.UpdatedBy = userId;
            }
        }

        private List<NavNodeVm> BuildTree(List<NavigationMenuItem> items, List<PageOptionVm> pages)
        {
            var byParent = items
                .GroupBy(x => x.ParentId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ToList());

            List<NavNodeVm> build(int? parentId)
            {
                if (!byParent.TryGetValue(parentId, out var children))
                    return new();

                return children.Select(x => new NavNodeVm
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    SortOrder = x.SortOrder,
                    Label = x.Label,
                    PageId = x.PageId,
                    Url = x.Url,
                    OpenInNewTab = x.OpenInNewTab,
                    IsActive = x.IsActive,
                    PageOptions = pages,
                    Children = build(x.Id)
                }).ToList();
            }

            return build(null);
        }

        private Guid GetUserIdOrEmpty()
        {
            var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(s, out var id) ? id : Guid.Empty;
        }

        private void InvalidateNavCache(Guid tenantId, int menuId)
        {
            _cache.Remove($"nav:{tenantId}:{menuId}");
        }
    }
}
