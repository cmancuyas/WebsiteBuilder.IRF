using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Rendering;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Sitemap
{
    public sealed class TenantSitemapIndexService : ITenantSitemapIndexService
    {
        private const int MaxUrlsPerSitemap = 50_000;
        private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ITenantUrlResolver _url;

        // Simple in-process cache
        private static readonly object _lock = new();
        private static readonly Dictionary<string, string> _cache = new();

        public TenantSitemapIndexService(DataContext db, ITenantContext tenant, ITenantUrlResolver url)
        {
            _db = db;
            _tenant = tenant;
            _url = url;
        }

        public void Invalidate()
        {
            lock (_lock) _cache.Clear();
        }

        public async Task<string> GetSitemapIndexXmlAsync(CancellationToken ct = default)
        {
            var (scheme, host) = await _url.GetCanonicalAsync(ct);
            var key = $"sitemap-index::{_tenant.TenantId}::{scheme}::{host}";

            if (TryGetCache(key, out var cached)) return cached;

            var total = await EligiblePagesQuery().CountAsync(ct);
            var parts = Math.Max(1, (int)Math.Ceiling(total / (double)MaxUrlsPerSitemap));

            // lastmod = max(PublishedAt ?? CreatedAt) among published revisions referenced by eligible pages
            DateTime? latest = null;

            if (total > 0)
            {
                latest = await (
                    from p in EligiblePagesQuery()
                    join r in _db.PageRevisions.AsNoTracking()
                        on p.PublishedRevisionId!.Value equals r.Id
                    select (DateTime?)(r.PublishedAt ?? r.CreatedAt)
                ).MaxAsync(ct);
            }

            var index = new XElement(Ns + "sitemapindex",
                Enumerable.Range(1, parts).Select(part =>
                {
                    // MUST match your endpoint: /sitemaps/pages-{part:int}.xml
                    var loc = Absolute(scheme, host, $"/sitemaps/pages-{part}.xml");

                    var el = new XElement(Ns + "sitemap",
                        new XElement(Ns + "loc", loc)
                    );

                    if (latest.HasValue)
                    {
                        el.Add(new XElement(
                            Ns + "lastmod",
                            latest.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                        ));
                    }

                    return el;
                })
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), index);
            var xml = doc.ToString(SaveOptions.DisableFormatting);

            SetCache(key, xml);
            return xml;
        }

        public async Task<string> GetSitemapPartXmlAsync(int part, CancellationToken ct = default)
        {
            if (part < 1) part = 1;

            var (scheme, host) = await _url.GetCanonicalAsync(ct);
            var key = $"sitemap-part::{_tenant.TenantId}::{scheme}::{host}::{part}";

            if (TryGetCache(key, out var cached)) return cached;

            var skip = (part - 1) * MaxUrlsPerSitemap;

            // Pull URL entries based on the published snapshot revision (slug + lastmod)
            var rows = await (
                from p in EligiblePagesQuery()
                join r in _db.PageRevisions.AsNoTracking()
                    on p.PublishedRevisionId!.Value equals r.Id
                orderby p.Id
                select new
                {
                    Slug = r.Slug, // published snapshot slug (canonical)
                    LastMod = (DateTime?)(r.PublishedAt ?? r.CreatedAt)
                }
            )
            .Skip(skip)
            .Take(MaxUrlsPerSitemap)
            .ToListAsync(ct);

            var urlset = new XElement(Ns + "urlset",
                rows.Select(r =>
                {
                    var path = NormalizeSlugToPath(r.Slug);
                    var loc = Absolute(scheme, host, path);

                    var url = new XElement(Ns + "url",
                        new XElement(Ns + "loc", loc)
                    );

                    if (r.LastMod.HasValue)
                    {
                        url.Add(new XElement(
                            Ns + "lastmod",
                            r.LastMod.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                        ));
                    }

                    // Optional SEO hints
                    var isHome = path == "/";
                    var depth = isHome
                        ? 0
                        : path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length;

                    url.Add(new XElement(Ns + "changefreq", isHome ? "daily" : "weekly"));

                    var priority =
                        isHome ? "1.0" :
                        depth <= 1 ? "0.8" :
                        depth == 2 ? "0.7" :
                        "0.6";

                    url.Add(new XElement(Ns + "priority", priority));

                    return url;
                })
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), urlset);
            var xml = doc.ToString(SaveOptions.DisableFormatting);

            SetCache(key, xml);
            return xml;
        }

        // ============================================================
        // Eligible pages: ONLY published pages with a published revision
        // ============================================================
        private IQueryable<Page> EligiblePagesQuery()
        {
            return _db.Pages
                .AsNoTracking()
                .Where(p => p.TenantId == _tenant.TenantId)
                .Where(p => p.PageStatusId == PageStatusIds.Published)
                .Where(p => p.PublishedRevisionId != null);

            // optional:
            // .Where(p => p.ShowInNavigation);
        }

        private static string Absolute(string scheme, string host, string path)
        {
            var cleanPath = string.IsNullOrWhiteSpace(path)
                ? "/"
                : (path.StartsWith("/") ? path : "/" + path);

            return $"{scheme}://{host}{cleanPath}";
        }

        private static string NormalizeSlugToPath(string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return "/";
            return slug.StartsWith("/") ? slug : "/" + slug;
        }

        private static bool TryGetCache(string key, out string xml)
        {
            lock (_lock) return _cache.TryGetValue(key, out xml!);
        }

        private static void SetCache(string key, string xml)
        {
            lock (_lock) _cache[key] = xml;
        }
    }
}
