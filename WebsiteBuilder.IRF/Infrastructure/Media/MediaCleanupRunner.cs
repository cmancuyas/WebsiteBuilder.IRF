using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class MediaCleanupRunner : IMediaCleanupRunner
{
    private readonly DataContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly MediaCleanupOptions _opts;

    public MediaCleanupRunner(DataContext db, IWebHostEnvironment env, IOptions<MediaCleanupOptions> options)
    {
        _db = db;
        _env = env;
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

        var candidates = await _db.Set<MediaAsset>()
            .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc)
            .OrderBy(m => m.DeletedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        result.CandidatesFound = candidates.Count;
        if (candidates.Count == 0)
            return result;

        foreach (var m in candidates)
        {
            TryDeletePhysical(m.StorageKey, ref result);
            TryDeletePhysical(m.ThumbStorageKey, ref result);
        }

        // Last chance safety check: ensure still deleted & eligible
        candidates = candidates
            .Where(m => m.IsDeleted && m.DeletedAt != null && m.DeletedAt <= cutoffUtc)
            .ToList();

        _db.Set<MediaAsset>().RemoveRange(candidates);
        result.RecordsDeleted = candidates.Count;

        await _db.SaveChangesAsync(ct);
        return result;
    }

    private void TryDeletePhysical(string? storageKey, ref MediaCleanupResult result)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return;
        if (!storageKey.StartsWith("/", StringComparison.Ordinal)) return;
        if (!storageKey.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return;

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
