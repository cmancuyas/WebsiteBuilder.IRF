using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Auth;
using WebsiteBuilder.IRF.Infrastructure.Media;
using WebsiteBuilder.IRF.Infrastructure.Middleware;
using WebsiteBuilder.IRF.Infrastructure.Pages;
using WebsiteBuilder.IRF.Infrastructure.Razor;
using WebsiteBuilder.IRF.Infrastructure.Rendering;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Sections.Validators;
using WebsiteBuilder.IRF.Infrastructure.Sitemap;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.Repository;
using WebsiteBuilder.IRF.Repository.IRepository;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages
builder.Services.AddRazorPages(options =>
{
    // Admin must be explicitly authorized
    options.Conventions.AuthorizeFolder("/Admin", "AdminArea");

    // Allow anonymous access to login/logout pages
    options.Conventions.AllowAnonymousToFolder("/Admin/Account");
});


// === Database Contexts ===

// Identity DB
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Identity"),
        sqlOptions =>
        {
            sqlOptions.UseNetTopologySuite();
            sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
            sqlOptions.CommandTimeout(120);
        }
    )
);

// Main SaaS DB
builder.Services.AddDbContext<DataContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Data"),
        sqlOptions =>
        {
            sqlOptions.UseNetTopologySuite();
            sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
            sqlOptions.CommandTimeout(120);
        }
    )
);

// Identity
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Cookie paths (moved AFTER AddIdentity)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Admin/Account/Login";
    options.AccessDeniedPath = "/Admin/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
});

// Authorization (updated to include SuperAdmin + use AppRoles)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminArea", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole(AppRoles.SuperAdmin) ||
            ctx.User.IsInRole(AppRoles.Admin) ||
            ctx.User.HasClaim("Permission", "Admin.Access"));
    });

    options.AddPolicy("PagesPreview", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole(AppRoles.SuperAdmin) ||
            ctx.User.IsInRole(AppRoles.Admin) ||
            ctx.User.HasClaim("Permission", "Pages.Preview"));
    });
});


builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;

    // Include common MIME types + sitemap/xml
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/xml",
        "text/xml",
        "application/rss+xml",
        "application/atom+xml",
        "text/plain" // robots.txt
    });
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// Prefer Brotli if available, fallback to Gzip
builder.Services.Configure<BrotliCompressionProviderOptions>(o =>
{
    o.Level = System.IO.Compression.CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(o =>
{
    o.Level = System.IO.Compression.CompressionLevel.Fastest;
});

// Tenant services
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITenantNavigationService, TenantNavigationService>();

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantResolver, TenantResolver>();
builder.Services.AddSingleton<ISectionRegistry, SectionRegistry>();
builder.Services.AddSingleton<ISectionJsonValidator, SectionJsonValidator>();

builder.Services.AddScoped<ISectionValidationService, SectionValidationService>();
builder.Services.AddScoped<ISectionContentValidator, HeroSectionValidator>();
builder.Services.AddScoped<ISectionContentValidator, TextSectionValidator>();
builder.Services.AddScoped<ISectionContentValidator, GallerySectionValidator>();
builder.Services.AddScoped<IPagePublishingService, PagePublishingService>();
builder.Services.AddScoped<PagePublishValidator>();
builder.Services.AddScoped<IPageRevisionSectionService, PageRevisionSectionService>();

builder.Services.AddScoped<IRazorPartialRenderer,
                           RazorPartialRenderer>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantSitemapService, TenantSitemapService>();
builder.Services.AddScoped<IPageRenderService, PageRenderService>();
builder.Services.AddScoped<IPageRenderPipeline, PageRenderPipeline>();
// =====================
// Media (Cleanup/Quota/Alerts)
// =====================

// Cleanup options + nightly hosted service
builder.Services.Configure<MediaCleanupOptions>(
    builder.Configuration.GetSection("Media:Cleanup"));

builder.Services.AddHostedService<MediaCleanupHostedService>();

// Cleanup runner (manual “Run Now” button uses this)
builder.Services.AddScoped<IMediaCleanupRunner, MediaCleanupRunner>();

// Quotas
builder.Services.Configure<MediaQuotaOptions>(
    builder.Configuration.GetSection("Media:Quota"));

builder.Services.AddScoped<ITenantMediaQuotaService, TenantMediaQuotaService>();

// Alerts
builder.Services.Configure<MediaAlertsOptions>(
    builder.Configuration.GetSection("Media:Alerts"));

builder.Services.AddHttpClient();

// FIX: Register concrete notifiers and map interface to Composite
builder.Services.AddScoped<DbMediaAlertNotifier>();
builder.Services.AddScoped<CompositeMediaAlertNotifier>();
builder.Services.AddScoped<IMediaAlertNotifier>(sp => sp.GetRequiredService<CompositeMediaAlertNotifier>());

builder.Services.AddScoped<ITenantSitemapIndexService,
                          TenantSitemapIndexService>();

builder.Services.AddScoped<ITenantUrlResolver, TenantUrlResolver>();


var app = builder.Build();

await IdentitySeeder.SeedAsync(app.Services, app.Configuration);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();

    var valid = db.PageStatuses.Any(p =>
        p.Id == PageStatusIds.Published &&
        p.Name == "Published");

    if (!valid)
        throw new InvalidOperationException("PageStatus seed mismatch.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseResponseCompression();
app.UseStaticFiles();

app.UseRouting();

// ✅ Tenant resolution must be BEFORE auth
// 1) Admin tenant resolver (cookie-based, /Admin only)
app.UseMiddleware<AdminTenantResolutionMiddleware>();

// 2) Public tenant resolver (host-based, non-admin)
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();


static string ComputeETag(string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);
    var hash = SHA256.HashData(bytes);
    // Quote per RFC for ETag header
    return "\"" + Convert.ToHexString(hash).ToLowerInvariant() + "\"";
}

// ============================================================
// SITEMAP + ROBOTS ENDPOINTS (Enhanced)
// - Strong caching headers (ETag + 304)
// - Compression-safe caching (Vary: Accept-Encoding)
// - Longer TTL (1 hour) since you already invalidate server-side
// - Optional: guard invalid parts
// ============================================================

const int SitemapMaxAgeSeconds = 3600; // 1 hour

static void ApplyCacheHeaders(HttpContext http, string etag)
{
    http.Response.Headers.ETag = etag;
    http.Response.Headers.CacheControl = $"public,max-age={SitemapMaxAgeSeconds}";
    http.Response.Headers.Vary = "Accept-Encoding"; // IMPORTANT for gzip/br variants
}

static bool IsNotModified(HttpContext http, string etag)
{
    return http.Request.Headers.TryGetValue("If-None-Match", out var inm) &&
           inm.ToString().Contains(etag, StringComparison.Ordinal);
}

static IResult XmlResult(string xml) =>
    Results.Text(xml, "application/xml; charset=utf-8");

static IResult TextResult(string txt) =>
    Results.Text(txt, "text/plain; charset=utf-8");



app.MapPost("/admin/api/validate-section-json",
    async (HttpContext http,
           ISectionValidationService validator,
           ITenantContext tenant) =>
    {
        if (!tenant.IsResolved)
            return Results.BadRequest(new { error = "Tenant not resolved." });

        var form = await http.Request.ReadFromJsonAsync<ValidateSectionRequest>(http.RequestAborted);

        if (form is null || string.IsNullOrWhiteSpace(form.TypeKey))
            return Results.BadRequest(new { error = "Invalid request." });

        var result = await validator.ValidateAsync(form.TypeKey, form.SettingsJson);

        return Results.Ok(new
        {
            isValid = result.IsValid,
            errors = result.Errors
        });
    })
    .RequireAuthorization("AdminArea");


// ----------------------------
// /sitemap.xml (canonical entry point) -> returns sitemapindex
// ----------------------------
app.MapGet("/sitemap.xml", async (HttpContext http, ITenantSitemapIndexService svc, CancellationToken ct) =>
{
    var xml = await svc.GetSitemapIndexXmlAsync(ct);
    var etag = ComputeETag(xml);

    if (IsNotModified(http, etag))
    {
        ApplyCacheHeaders(http, etag);
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    ApplyCacheHeaders(http, etag);
    return XmlResult(xml);
});

// ----------------------------
// /sitemap_index.xml (alias) -> returns same sitemapindex
// ----------------------------
app.MapGet("/sitemap_index.xml", async (HttpContext http, ITenantSitemapIndexService svc, CancellationToken ct) =>
{
    var xml = await svc.GetSitemapIndexXmlAsync(ct);
    var etag = ComputeETag(xml);

    if (IsNotModified(http, etag))
    {
        ApplyCacheHeaders(http, etag);
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    ApplyCacheHeaders(http, etag);
    return XmlResult(xml);
});

// ----------------------------
// /sitemaps/pages-{part}.xml (paged urlset)
// ----------------------------
app.MapGet("/sitemaps/pages-{part:int}.xml", async (HttpContext http, int part, ITenantSitemapIndexService svc, CancellationToken ct) =>
{
    // optional: normalize invalid parts (prevents negative indexing, etc.)
    if (part < 1) part = 1;

    var xml = await svc.GetSitemapPartXmlAsync(part, ct);
    var etag = ComputeETag(xml);

    if (IsNotModified(http, etag))
    {
        ApplyCacheHeaders(http, etag);
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    ApplyCacheHeaders(http, etag);
    return XmlResult(xml);
});

// ----------------------------
// /robots.txt (resolver-based canonical host + points to /sitemap.xml)
// ----------------------------
app.MapGet("/robots.txt", async (HttpContext http, ITenantUrlResolver url, CancellationToken ct) =>
{
    var (scheme, host) = await url.GetCanonicalAsync(ct);

    var txt = string.Join("\n", new[]
    {
        "User-agent: *",
        "Disallow: /Admin/",
        "Disallow: /admin/",
        "Disallow: /Preview/",
        "Disallow: /preview/",
        "Disallow: /_framework/",
        $"Sitemap: {scheme}://{host}/sitemap.xml"
    });

    // robots.txt can also benefit from compression-aware caching
    http.Response.Headers.CacheControl = $"public,max-age={SitemapMaxAgeSeconds}";
    http.Response.Headers.Vary = "Accept-Encoding";

    // Optional (usually fine to omit):
    // var etag = ComputeETag(txt);
    // if (IsNotModified(http, etag)) { ApplyCacheHeaders(http, etag); return Results.StatusCode(304); }
    // ApplyCacheHeaders(http, etag);

    return TextResult(txt);
});

// ============================================================
// SEO URL Normalization (301 redirects)
// - lowercase paths
// - trim trailing slash (except "/")
// - collapse multiple slashes
// - /home -> /
// - preserve query string
// - GET/HEAD only
// - exclude admin/preview/sitemaps/static
// ============================================================
app.Use(async (ctx, next) =>
{
    // Only normalize safe idempotent requests
    if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
    {
        await next();
        return;
    }

    var path = ctx.Request.Path.Value ?? "/";

    // Skip certain paths (do not rewrite admin/system/static endpoints)
    static bool IsExcluded(string p)
    {
        return p.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/Preview", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/sitemap", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/sitemaps", StringComparison.OrdinalIgnoreCase)
            || p.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/images", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);
    }

    if (IsExcluded(path))
    {
        await next();
        return;
    }

    // Collapse multiple slashes
    var normalized = path;
    while (normalized.Contains("//", StringComparison.Ordinal))
        normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

    // Lowercase
    normalized = normalized.ToLowerInvariant();

    // Canonicalize /home -> /
    if (normalized.Equals("/home", StringComparison.Ordinal))
        normalized = "/";

    // Remove trailing slash except root
    if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
        normalized = normalized.TrimEnd('/');

    // Redirect if changed
    if (!string.Equals(path, normalized, StringComparison.Ordinal))
    {
        var qs = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "";
        var location = normalized + qs;

        ctx.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        ctx.Response.Headers.Location = location;
        return;
    }

    await next();
});


app.UseStatusCodePagesWithReExecute("/Admin/Errors/{0}");

app.MapRazorPages();

// IMPORTANT: fallback to CMS page renderer
app.MapFallbackToPage("/{slug?}", "/[slug]");

app.Run();
