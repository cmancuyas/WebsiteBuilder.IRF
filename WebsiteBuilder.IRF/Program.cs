using Microsoft.AspNetCore.HttpOverrides;
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

    // Include common MIME types + sitemap/xml + robots
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

builder.Services.AddScoped<IRazorPartialRenderer, RazorPartialRenderer>();

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

builder.Services.AddScoped<ITenantSitemapIndexService, TenantSitemapIndexService>();

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

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.UseHttpsRedirection();

app.UseResponseCompression();

app.UseRouting();

// ✅ Tenant resolution must be BEFORE auth AND before robots generation
// 1) Admin tenant resolver (cookie-based, /Admin only)
app.UseMiddleware<AdminTenantResolutionMiddleware>();

// 2) Public tenant resolver (host-based, non-admin)
app.UseMiddleware<TenantResolutionMiddleware>();

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
    if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
    {
        await next();
        return;
    }

    var path = ctx.Request.Path.Value ?? "/";

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

    var normalized = path;

    while (normalized.Contains("//", StringComparison.Ordinal))
        normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

    normalized = normalized.ToLowerInvariant();

    if (normalized.Equals("/home", StringComparison.Ordinal))
        normalized = "/";

    if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
        normalized = normalized.TrimEnd('/');

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

// ----------------------------
// /robots.txt (DYNAMIC, tenant-aware, overrides wwwroot/robots.txt)
// IMPORTANT: must run BEFORE UseStaticFiles()
// ----------------------------
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
    {
        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var tenant = context.RequestServices.GetRequiredService<ITenantContext>();
        var url = context.RequestServices.GetRequiredService<ITenantUrlResolver>();

        if (!tenant.IsResolved)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var (scheme, host) = await url.GetCanonicalAsync(context.RequestAborted);
        var sitemapUrl = $"{scheme}://{host}/sitemap.xml";

        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "public, max-age=300";
        context.Response.Headers["Vary"] = "Accept-Encoding";

        if (!env.IsProduction())
        {
            await context.Response.WriteAsync(
                $@"User-agent: *
                Disallow: /

                Sitemap: {sitemapUrl}
                ");
             return;
        }

        await context.Response.WriteAsync(
            $@"User-agent: *
            Allow: /

            Disallow: /Admin/
            Disallow: /admin/
            Disallow: /api/
            Disallow: /Preview/
            Disallow: /preview/
            Disallow: /_framework/

            Sitemap: {sitemapUrl}
            ");
        return;
    }

    await next();
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

static string ComputeETag(string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);
    var hash = SHA256.HashData(bytes);
    return "\"" + Convert.ToHexString(hash).ToLowerInvariant() + "\"";
}

const int SitemapMaxAgeSeconds = 3600; // 1 hour

static void ApplyCacheHeaders(HttpContext http, string etag)
{
    http.Response.Headers.ETag = etag;
    http.Response.Headers.CacheControl = $"public,max-age={SitemapMaxAgeSeconds}";
    http.Response.Headers.Vary = "Accept-Encoding";
}

static bool IsNotModified(HttpContext http, string etag)
{
    return http.Request.Headers.TryGetValue("If-None-Match", out var inm) &&
           inm.ToString().Contains(etag, StringComparison.Ordinal);
}

static IResult XmlResult(string xml) =>
    Results.Text(xml, "application/xml; charset=utf-8");

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

app.UseStatusCodePagesWithReExecute("/Admin/Errors/{0}");

app.MapRazorPages();

// IMPORTANT: fallback to CMS page renderer
app.MapFallbackToPage("/{slug?}", "/[slug]");

app.Run();
