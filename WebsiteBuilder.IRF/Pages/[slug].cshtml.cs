using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Pages
{
    public class _slug_Model : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;

        public _slug_Model(DataContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public WebsiteBuilder.Models.Page? PageEntity { get; private set; }
        public bool IsPreview { get; private set; }

        public List<RenderSectionDto> RenderSections { get; private set; } = new();

        public sealed class RenderSectionDto
        {
            public int SectionTypeId { get; init; }
            public string? SectionTypeName { get; init; }
            public int SortOrder { get; init; }
            public string? SettingsJson { get; init; }
        }

        // Catch-all route param: /{**slug}
        public async Task<IActionResult> OnGetAsync(string? slug)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            var previewRequested = IsPreviewRequested();
            IsPreview = previewRequested && UserCanPreview();

            // Hard stop: never allow anonymous preview
            if (previewRequested && !IsPreview)
                return Forbid();

            var normalizedSlug = NormalizeSlug(slug);
            if (string.IsNullOrWhiteSpace(normalizedSlug))
                normalizedSlug = "home";

            var pageQuery = _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    p.IsActive &&
                    !p.IsDeleted &&
                    p.Slug == normalizedSlug);

            if (!IsPreview)
            {
                pageQuery = pageQuery.Where(p => p.PageStatusId == PageStatusIds.Published);
            }
            else
            {
                pageQuery = pageQuery.Where(p => p.PageStatusId != PageStatusIds.Archived);
                ApplyNoCacheHeaders();
            }

            PageEntity = await pageQuery.FirstOrDefaultAsync(HttpContext.RequestAborted);

            if (PageEntity is null)
                return NotFound();

            if (IsPreview)
            {
                var draftSections = await _db.PageSections
                    .AsNoTracking()
                    .Include(s => s.SectionType)
                    .Where(s =>
                        s.TenantId == _tenant.TenantId &&
                        s.PageId == PageEntity.Id &&
                        s.IsActive &&
                        !s.IsDeleted)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.Id)
                    .ToListAsync(HttpContext.RequestAborted);

                RenderSections = draftSections.Select(s => new RenderSectionDto
                {
                    SectionTypeId = s.SectionTypeId,
                    SectionTypeName = s.SectionType?.Name,
                    SortOrder = s.SortOrder,
                    SettingsJson = s.SettingsJson
                }).ToList();

                return Page();
            }

            if (PageEntity.PublishedRevisionId == null)
                return NotFound();

            var publishedSections = await _db.PageRevisionSections
                .AsNoTracking()
                .Include(s => s.SectionType)
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == PageEntity.PublishedRevisionId.Value &&
                    s.IsActive &&
                    !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToListAsync(HttpContext.RequestAborted);

            RenderSections = publishedSections.Select(s => new RenderSectionDto
            {
                SectionTypeId = s.SectionTypeId,
                SectionTypeName = s.SectionType?.Name,
                SortOrder = s.SortOrder,
                SettingsJson = s.SettingsJson
            }).ToList();

            return Page();
        }

        private bool IsPreviewRequested()
        {
            var val = Request.Query["preview"].ToString();
            if (string.IsNullOrWhiteSpace(val)) return false;

            return val.Equals("1", StringComparison.OrdinalIgnoreCase)
                || val.Equals("true", StringComparison.OrdinalIgnoreCase)
                || val.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private bool UserCanPreview()
        {
            if (User.Identity?.IsAuthenticated != true)
                return false;

            if (User.IsInRole("Admin"))
                return true;

            if (User.HasClaim("Permission", "Pages.Preview"))
                return true;

            return false;
        }

        private void ApplyNoCacheHeaders()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";
        }

        private static string NormalizeSlug(string? slug)
        {
            slug ??= string.Empty;
            slug = slug.Trim().Trim('/').ToLowerInvariant();
            slug = Regex.Replace(slug, @"\s+", "-");
            slug = Regex.Replace(slug, @"-+", "-");
            return slug.Trim('-');
        }
    }
}
