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
    public sealed class TenantSitemapIndexService : ITenantSitemapIndexService
    {
        private const int MaxUrlsPerSitemap = 50_000;

        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly IHttpContextAccessor _http;
        private readonly IMemoryCache _cache;

        private string IndexKey => $"sitemap:index:tenant:{_tenant.TenantId}";
        private string PartKey(int part) => $"sitemap:part:{part}:tenant:{_tenant.TenantId}";

        public TenantSitemapIndexService(
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

        public void Invalidate()
        {
            _cache.Remove(IndexKey);
            // Don’t try to remove all parts blindly; simplest is to use short expirations
            // OR store last part count in cache and remove those keys.
        }

        public async Task<string> GetSitemapIndexXmlAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(IndexKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
                return cached!;

            var (baseUrl, host) = await GetBaseUrlAsync(ct);

            var total = await QueryPublishedPages()
                .CountAsync(ct);

            var parts = Math.Max(1, (int)Math.Ceiling(total / (double)MaxUrlsPerSitemap));

            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var index = new XElement(ns + "sitemapindex",
                Enumerable.Range(1, parts).Select(i =>
                {
                    var loc = $"{baseUrl}/sitemaps/pages-{i}.xml";
                    return new XElement(ns + "sitemap",
                        new XElement(ns + "loc", loc));
                })
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), index);
            var xml = ToXml(doc);

            _cache.Set(IndexKey, xml, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            return xml;
        }

        public async Task<string> GetSitemapPartXmlAsync(int part, CancellationToken ct = default)
        {
            if (part < 1) part = 1;

            var key = PartKey(part);
            if (_cache.TryGetValue(key, out string? cached) && !string.IsNullOrWhiteSpace(cached))
                return cached!;

            var (baseUrl, _) = await GetBaseUrlAsync(ct);

            var skip = (part - 1) * MaxUrlsPerSitemap;

            var rows = await QueryPublishedPages()
                .OrderBy(p => p.Id)
                .Skip(skip)
                .Take(MaxUrlsPerSitemap)
                .Select(p => new PageRow(p.Slug, p.PublishedAt))
                .ToListAsync(ct);

            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var urlset = new XElement(ns + "urlset",
                rows.Select(r =>
                {
                    var slug = (r.Slug ?? string.Empty).Trim('/');
                    var loc = slug.Equals("home", StringComparison.OrdinalIgnoreCase) || slug == ""
                        ? $"{baseUrl}/"
                        : $"{baseUrl}/{slug}";

                    var url = new XElement(ns + "url",
                        new XElement(ns + "loc", loc));

                    if (r.PublishedAt.HasValue)
                    {
                        url.Add(new XElement(ns + "lastmod",
                            r.PublishedAt.Value.ToUniversalTime()
                                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                    }

                    return url;
                })
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), urlset);
            var xml = ToXml(doc);

            _cache.Set(key, xml, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            return xml;
        }

        private IQueryable<Page> QueryPublishedPages()
        {
            return _db.Set<Page>()
                .AsNoTracking()
                .Where(p =>
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted &&
                    p.IsActive &&
                    p.PageStatusId == PageStatusIds.Published &&
                    p.PublishedRevisionId != null &&
                    !string.IsNullOrWhiteSpace(p.Slug)
                // && p.ShowInNavigation // optional
                );
        }

        private async Task<(string BaseUrl, string Host)> GetBaseUrlAsync(CancellationToken ct)
        {
            var http = _http.HttpContext ?? throw new InvalidOperationException("No HttpContext.");
            var scheme = http.Request.Scheme;
            var requestHost = http.Request.Host.Value;

            var primaryHost = await _db.Set<DomainMapping>()
                .AsNoTracking()
                .Where(x => x.TenantId == _tenant.TenantId && !x.IsDeleted && x.IsActive && x.IsPrimary)
                .Select(x => x.Host)
                .FirstOrDefaultAsync(ct);

            var host = string.IsNullOrWhiteSpace(primaryHost) ? requestHost : primaryHost.Trim();
            var baseUrl = $"{scheme}://{host}".TrimEnd('/');

            return (baseUrl, host);
        }

        private static string ToXml(XDocument doc)
        {
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
