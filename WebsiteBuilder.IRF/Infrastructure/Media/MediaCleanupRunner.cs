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

    public MediaCleanupRunner(
        DataContext db,
        IWebHostEnvironment env,
        IOptions<MediaCleanupOptions> options,
        IOptions<MediaAlertsOptions> alertOptions,
        IMediaAlertNotifier notifier)
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

        var startedUtc = DateTime.UtcNow;
        var cutoffUtc = startedUtc.AddDays(-Math.Abs(_opts.RetentionDays));
        var batchSize = Math.Clamp(_opts.BatchSize, 50, 1000);

        // --- create run log (Running) ---
        var runLog = new MediaCleanupRunLog
        {
            RunType = "Nightly",
            RetentionDays = _opts.RetentionDays,
            BatchSize = batchSize,
            StartedAtUtc = startedUtc,
            Status = "Running",
            IsActive = true,
            IsDeleted = false,
            CreatedAt = startedUtc
        };

        _db.Set<MediaCleanupRunLog>().Add(runLog);
        await _db.SaveChangesAsync(ct); // ensure RunLog.Id exists

        try
        {
            result.CutoffUtc = cutoffUtc;

            var eligibleQuery = _db.Set<MediaAsset>()
                .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc);

            runLog.EligibleCount = await eligibleQuery.CountAsync(ct);

            var candidates = await eligibleQuery
                .OrderBy(m => m.DeletedAt)
                .Take(batchSize)
                .ToListAsync(ct);

            runLog.ProcessedCount = candidates.Count;
            result.CandidatesFound = candidates.Count;
            result.CandidateBytes = candidates.Sum(m => m.SizeBytes);

            await MaybeAlertAsync(result, runLog.Id, ct);

            if (candidates.Count == 0)
            {
                runLog.Status = "Succeeded";
                runLog.FinishedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return result;
            }

            foreach (var m in candidates)
            {
                if (TryDeletePhysical(m.StorageKey))
                    runLog.DeletedOriginalFilesCount++;

                if (TryDeletePhysical(m.ThumbStorageKey))
                    runLog.DeletedThumbnailFilesCount++;
            }

            // Re-check safety
            candidates = candidates
                .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc)
                .ToList();

            _db.Set<MediaAsset>().RemoveRange(candidates);
            runLog.HardDeletedDbRowsCount = candidates.Count;
            result.RecordsDeleted = candidates.Count;

            await _db.SaveChangesAsync(ct);

            runLog.Status = runLog.FailedCount == 0 ? "Succeeded" : "Partial";
            runLog.FinishedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            runLog.Status = "Failed";
            runLog.FailedCount = Math.Max(runLog.FailedCount, 1);
            runLog.ErrorSummary = ex.Message.Length > 2000
                ? ex.Message[..2000]
                : ex.Message;
            runLog.FinishedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task MaybeAlertAsync(MediaCleanupResult result, long runId, CancellationToken ct)
    {
        if (!_alertOpts.Enabled)
            return;

        if (_alertOpts.EligibleBytesThreshold <= 0)
            return;

        if (result.CandidateBytes < _alertOpts.EligibleBytesThreshold)
            return;

        var subject = "Media cleanup threshold exceeded";
        var message =
            $"RunId={runId}, " +
            $"CandidatesFound={result.CandidatesFound}, " +
            $"CandidateBytes={result.CandidateBytes}, " +
            $"CutoffUtc={result.CutoffUtc:yyyy-MM-dd HH:mm}";

        try
        {
            await _notifier.NotifyAsync(subject, message, ct);
        }
        catch
        {
            // never break cleanup
        }
    }

    private bool TryDeletePhysical(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return false;

        if (!storageKey.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = storageKey.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var physical = Path.Combine(_env.WebRootPath, relative);

        try
        {
            if (File.Exists(physical))
            {
                File.Delete(physical);
                return true;
            }
        }
        catch
        {
            // counted at run level
        }

        return false;
    }
}
