using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Sitemap
{
    public sealed class TenantSitemapService : ITenantSitemapService
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IHttpContextAccessor _http;
        private readonly IMemoryCache _cache;

        private string CacheKey => $"sitemap:tenant:{_tenant.TenantId}";

        public TenantSitemapService(
            DataContext db,
            ITenantContext tenant,
            IHttpContextAccessor http,
            IMemoryCache cache)
        {
            _db = db;
            _tenant = tenant;
            _http = http;
            _cache = cache;
        }

        public void Invalidate() => _cache.Remove(CacheKey);

        public async Task<string> GetSitemapXmlAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
                return cached!;

            var http = _http.HttpContext ?? throw new InvalidOperationException("No HttpContext.");

            // Prefer primary domain mapping if available
            var scheme = http.Request.Scheme;
            var requestHost = http.Request.Host.Value;

            var primaryHost = await _db.Set<DomainMapping>()
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == _tenant.TenantId &&
                    !x.IsDeleted &&
                    x.IsActive &&
                    x.IsPrimary)
                .Select(x => x.Host)
                .FirstOrDefaultAsync(ct);

            var host = string.IsNullOrWhiteSpace(primaryHost) ? requestHost : primaryHost.Trim();
            var baseUrl = $"{scheme}://{host}".TrimEnd('/');

            // Published-only pages for this tenant
            // - TenantId comes from TenantBaseModel
            // - Uses PublishedRevisionId (not PublishedVersionId)
            // - Uses PageStatusId == Published as the authoritative published state
            // - Optionally respects ShowInNavigation (toggle the line if you want it)
            var pages = await _db.Set<Page>()
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.IsActive &&
                    p.PageStatusId == PageStatusIds.Published &&
                    p.PublishedRevisionId != null &&
                    !string.IsNullOrWhiteSpace(p.Slug)
                // && p.ShowInNavigation // ✅ optional exclusion
                )
                .Select(p => new PageRow(p.Slug, p.PublishedAt))
                .ToListAsync(ct);

            var xml = BuildSitemapXml(baseUrl, pages);

            _cache.Set(CacheKey, xml, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            return xml;
        }

        private static string BuildSitemapXml(string baseUrl, IEnumerable<PageRow> pages)
        {
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var urlset = new XElement(ns + "urlset",
                pages.Select(p =>
                {
                    var slug = (p.Slug ?? string.Empty).Trim('/');
                    var loc = slug.Equals("home", StringComparison.OrdinalIgnoreCase) || slug == ""
                        ? $"{baseUrl}/"
                        : $"{baseUrl}/{slug}";

                    var url = new XElement(ns + "url",
                        new XElement(ns + "loc", loc));

                    if (p.PublishedAt.HasValue)
                    {
                        url.Add(new XElement(ns + "lastmod",
                            p.PublishedAt.Value.ToUniversalTime()
                                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                    }

                    return url;
                })
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), urlset);

            var sb = new StringBuilder();
            using var sw = new Utf8StringWriter(sb);
            doc.Save(sw);

            return sb.ToString();
        }

        private sealed record PageRow(string Slug, DateTime? PublishedAt);

        private sealed class Utf8StringWriter : System.IO.StringWriter
        {
            public Utf8StringWriter(StringBuilder sb) : base(sb) { }
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
