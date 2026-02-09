using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Auth;
using WebsiteBuilder.IRF.Infrastructure.Media;
using WebsiteBuilder.IRF.Infrastructure.Middleware;
using WebsiteBuilder.IRF.Infrastructure.Pages;
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
    // Everything under /Admin requires auth
    options.Conventions.AuthorizeFolder("/Admin");

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
    options.AddPolicy("PagesPreview", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole(AppRoles.SuperAdmin) ||
            ctx.User.IsInRole(AppRoles.Admin) ||
            ctx.User.HasClaim("Permission", "Pages.Preview"));
    });
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

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantSitemapService, TenantSitemapService>();

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
app.UseStaticFiles();

app.UseRouting();

// Tenant resolution must be BEFORE auth (good)
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();



static string ComputeETag(string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);
    var hash = SHA256.HashData(bytes);
    // Quote per RFC for ETag header
    return "\"" + Convert.ToHexString(hash).ToLowerInvariant() + "\"";
}

app.MapGet("/sitemap.xml", async (HttpContext http, ITenantSitemapService sitemap, CancellationToken ct) =>
{
    var xml = await sitemap.GetSitemapXmlAsync(ct);

    var etag = ComputeETag(xml);

    // Conditional GET
    if (http.Request.Headers.TryGetValue("If-None-Match", out var inm) &&
        inm.ToString().Contains(etag, StringComparison.Ordinal))
    {
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "public,max-age=300";
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    http.Response.Headers.ETag = etag;
    http.Response.Headers.CacheControl = "public,max-age=300";

    return Results.Text(xml, "application/xml; charset=utf-8");
});

app.MapGet("/robots.txt", (HttpContext http) =>
{
    var scheme = http.Request.Scheme;
    var host = http.Request.Host.Value;

    var txt = string.Join("\n", new[]
    {
        "User-agent: *",
        "Disallow: /Admin/",
        "Disallow: /admin/",
        "Disallow: /Preview/",
        "Disallow: /preview/",
        "Disallow: /_framework/",
        $"Sitemap: {scheme}://{host}/sitemap_index.xml"
    });

    return Results.Text(txt, "text/plain; charset=utf-8");
});


app.MapGet("/sitemap_index.xml", async (HttpContext http, ITenantSitemapIndexService svc, CancellationToken ct) =>
{
    var xml = await svc.GetSitemapIndexXmlAsync(ct);
    var etag = ComputeETag(xml);

    if (http.Request.Headers.TryGetValue("If-None-Match", out var inm) &&
        inm.ToString().Contains(etag, StringComparison.Ordinal))
    {
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "public,max-age=300";
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    http.Response.Headers.ETag = etag;
    http.Response.Headers.CacheControl = "public,max-age=300";
    return Results.Text(xml, "application/xml; charset=utf-8");
});

app.MapGet("/sitemaps/pages-{part:int}.xml", async (HttpContext http, int part, ITenantSitemapIndexService svc, CancellationToken ct) =>
{
    var xml = await svc.GetSitemapPartXmlAsync(part, ct);
    var etag = ComputeETag(xml);

    if (http.Request.Headers.TryGetValue("If-None-Match", out var inm) &&
        inm.ToString().Contains(etag, StringComparison.Ordinal))
    {
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "public,max-age=300";
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    http.Response.Headers.ETag = etag;
    http.Response.Headers.CacheControl = "public,max-age=300";
    return Results.Text(xml, "application/xml; charset=utf-8");
});


// IMPORTANT: fallback to CMS page renderer
app.MapFallbackToPage("/{slug?}", "/[slug]");

app.Run();
