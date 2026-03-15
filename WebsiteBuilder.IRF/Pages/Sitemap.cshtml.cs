using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Rendering;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Pages
{
    [ResponseCache(Duration = 300)] // 5 min cache
    public class SitemapXmlModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ITenantUrlResolver _url;

        public SitemapXmlModel(
            DataContext db,
            ITenantContext tenant,
            ITenantUrlResolver url)
        {
            _db = db;
            _tenant = tenant;
            _url = url;
        }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!_tenant.IsResolved)
                return NotFound();

            var (scheme, host) = await _url.GetCanonicalAsync(ct);

            var revisions = await _db.PageRevisions
                .AsNoTracking()
                .Where(r =>
                    r.TenantId == _tenant.TenantId &&
                    r.IsPublishedSnapshot &&
                    !r.IsDeleted &&
                    r.IsActive)
                .OrderByDescending(r => r.PublishedAt)
                .ToListAsync(ct);

            var sb = new StringBuilder();

            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">");

            foreach (var rev in revisions)
            {
                var slug = NormalizeSlug(rev.Slug);
                var path = string.IsNullOrWhiteSpace(slug) ? "/" : "/" + slug;
                var loc = $"{scheme}://{host}{path}";

                sb.AppendLine("  <url>");
                sb.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(loc)}</loc>");

                if (rev.PublishedAt.HasValue)
                {
                    sb.AppendLine($"    <lastmod>{rev.PublishedAt.Value:yyyy-MM-dd}</lastmod>");
                }

                sb.AppendLine("  </url>");
            }

            sb.AppendLine("</urlset>");

            return Content(sb.ToString(), "application/xml", Encoding.UTF8);
        }

        private static string NormalizeSlug(string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return string.Empty;

            var s = slug.Trim().Trim('/').ToLowerInvariant();
            return s;
        }
    }
}
