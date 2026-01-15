using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class MediaCleanupOptions
{
    public bool Enabled { get; set; } = true;
    public int RunHourLocal { get; set; } = 2;     // Asia/Manila hour
    public int RetentionDays { get; set; } = 7;
    public int BatchSize { get; set; } = 200;
}

public sealed class MediaCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;
    private readonly MediaCleanupOptions _opts;
    private readonly TimeZoneInfo _tz;

    public MediaCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment env,
        IOptions<MediaCleanupOptions> options)
    {
        _scopeFactory = scopeFactory;
        _env = env;
        _opts = options.Value;
        _tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun();
            await Task.Delay(delay, stoppingToken);

            try
            {
                await RunOnce(stoppingToken);
            }
            catch
            {
                // Intentionally swallow so the service continues next day.
                // Prefer adding your logger here (ILogger<MediaCleanupHostedService>).
            }
        }
    }

    private TimeSpan GetDelayUntilNextRun()
    {
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _tz);

        var nextLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, _opts.RunHourLocal, 0, 0);
        if (nowLocal >= nextLocal)
            nextLocal = nextLocal.AddDays(1);

        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, _tz);
        var delay = nextUtc - nowUtc;
        return delay < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : delay;
    }

    private async Task RunOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Abs(_opts.RetentionDays));
        var batchSize = Math.Clamp(_opts.BatchSize, 50, 1000);

        // Note: relies on BaseModel having IsDeleted + DeletedAt.
        // If your DeletedAt is nullable, keep the filter below.
        var candidates = await db.Set<MediaAsset>()
            .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc)
            .OrderBy(m => m.DeletedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        foreach (var m in candidates)
        {
            DeletePhysicalFileIfPossible(m.StorageKey);
            DeletePhysicalFileIfPossible(m.ThumbStorageKey);
        }

        db.Set<MediaAsset>().RemoveRange(candidates);
        await db.SaveChangesAsync(ct);
    }

    private void DeletePhysicalFileIfPossible(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return;

        // storageKey is a public URL path like "/uploads/2026/01/abc.jpg"
        if (!storageKey.StartsWith("/", StringComparison.Ordinal))
            return;

        // Prevent path traversal; only allow uploads folder
        if (!storageKey.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return;

        var relative = storageKey.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var physical = Path.Combine(_env.WebRootPath, relative);

        try
        {
            if (File.Exists(physical))
                File.Delete(physical);
        }
        catch
        {
            // swallow; cleanup should be best-effort
        }
    }
}
