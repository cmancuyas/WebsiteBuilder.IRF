using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class MediaCleanupRunner : IMediaCleanupRunner
{
    private readonly DataContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly MediaAlertsOptions _alertOpts;
    private readonly IMediaAlertNotifier _notifier;
    private readonly MediaCleanupOptions _opts;

    public MediaCleanupRunner(DataContext db, IWebHostEnvironment env, IOptions<MediaCleanupOptions> options, IOptions<MediaAlertsOptions> alertOptions, IMediaAlertNotifier notifier)
    {
        _db = db;
        _env = env;
        _alertOpts = alertOptions.Value;
        _notifier = notifier;
        _opts = options.Value;
    }

    public async Task<MediaCleanupResult> RunOnceAsync(CancellationToken ct)
    {
        var result = new MediaCleanupResult();

        if (!_opts.Enabled)
            return result;

        var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Abs(_opts.RetentionDays));
        var batchSize = Math.Clamp(_opts.BatchSize, 50, 1000);

        result.CutoffUtc = cutoffUtc;

        var eligibleQuery = _db.Set<MediaAsset>()
            .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc);

        var eligibleCountTotal = await eligibleQuery.CountAsync(ct);

        var candidates = await _db.Set<MediaAsset>()
            .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc)
            .OrderBy(m => m.DeletedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        result.CandidatesFound = candidates.Count;
        result.CandidateBytes = candidates.Sum(m => m.SizeBytes);

        // Alert (best-effort): if eligible bytes exceed threshold
        await MaybeAlertAsync(result, ct);


        if (candidates.Count == 0)
            return result;

        foreach (var m in candidates)
        {
            TryDeletePhysical(m.StorageKey, ref result);
            TryDeletePhysical(m.ThumbStorageKey, ref result);
        }

        // Last-chance safety: a record could have been restored mid-run.
        candidates = candidates
            .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc)
            .ToList();

        _db.Set<MediaAsset>().RemoveRange(candidates);
        result.RecordsDeleted = candidates.Count;

        await _db.SaveChangesAsync(ct);
        return result;
    }
    private async Task MaybeAlertAsync(MediaCleanupResult result, CancellationToken ct)
    {
        if (!_alertOpts.Enabled)
            return;

        if (_alertOpts.EligibleBytesThreshold <= 0)
            return;

        if (result.CandidateBytes < _alertOpts.EligibleBytesThreshold)
            return;

        var subject = "Media cleanup threshold exceeded";
        var message =
            $"CandidatesFound={result.CandidatesFound}, " +
            $"CandidateBytes={result.CandidateBytes}, " +
            $"CutoffUtc={result.CutoffUtc:yyyy-MM-dd HH:mm}";

        try
        {
            await _notifier.NotifyAsync(subject, message, ct);
        }
        catch
        {
            // Never break cleanup if alert fails
        }
    }

    private static long ParseSizeBytes(string? sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(sizeBytes))
            return 0;

        var cleaned = sizeBytes.Trim().Replace(",", "");
        return long.TryParse(cleaned, out var v) && v > 0 ? v : 0;
    }

    private void TryDeletePhysical(string? storageKey, ref MediaCleanupResult result)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return;

        // Safety: delete only from uploads folder
        if (!storageKey.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return;

        var relative = storageKey.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var physical = Path.Combine(_env.WebRootPath, relative);

        try
        {
            if (File.Exists(physical))
            {
                File.Delete(physical);
                result.FilesDeleted++;
            }
        }
        catch
        {
            result.FileDeleteFailures++;
        }
    }
}
