using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Middleware;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.IRF.Repository;
using WebsiteBuilder.IRF.Repository.IRepository;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services
    .AddAuthentication()
    .AddCookie(); // or AddIdentityCookies()

builder.Services.AddAuthorization();

// === Database Contexts ===

// ASP.NET Identity context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Identity"),
        sqlOptions =>
        {
            sqlOptions.UseNetTopologySuite();

            // 🧩 Enable resiliency for transient connection issues
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null
            );

            // 🧩 Give migrations more time to complete
            sqlOptions.CommandTimeout(120);
        }
    )
);

// Main application context
builder.Services.AddDbContext<DataContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Data"),
        sqlOptions =>
        {
            sqlOptions.UseNetTopologySuite();

            // 🧩 Enable retry resiliency for transient SQL failures
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null
            );

            // 🧩 Increase timeout to prevent migration timeouts
            sqlOptions.CommandTimeout(120);
        }
    )
);

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantResolver, TenantResolver>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
