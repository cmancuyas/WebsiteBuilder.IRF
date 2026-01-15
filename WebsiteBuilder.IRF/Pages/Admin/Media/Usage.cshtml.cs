using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Media;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Media
{
    [Authorize]
    public class UsageModel : PageModel
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly MediaCleanupOptions _cleanupOptions;
        private readonly IMediaCleanupRunner _cleanupRunner;
        private readonly ITenantMediaQuotaService _quota;

        public UsageModel(
            DataContext db,
            ITenantContext tenant,
            IOptions<MediaCleanupOptions> cleanupOptions,
            IMediaCleanupRunner cleanupRunner,
            ITenantMediaQuotaService quota)
        {
            _db = db;
            _tenant = tenant;
            _cleanupOptions = cleanupOptions.Value;
            _cleanupRunner = cleanupRunner;
            _quota = quota;
        }

        public bool IsTenantResolved => _tenant.IsResolved;

        public UsageSummary Summary { get; private set; } = new();
        public List<MonthlyUsageRow> MonthlyUsage { get; private set; } = new();
        public List<ContentTypeRow> ByContentType { get; private set; } = new();
        public List<MediaRow> LargestActive { get; private set; } = new();
        public List<MediaRow> LargestDeleted { get; private set; } = new();

        public CleanupEligibility Eligibility { get; private set; } = new();

        // Quota panel
        public long QuotaBytes { get; private set; }
        public double UsedPercent => (QuotaBytes <= 0) ? 0 : (double)Summary.ActiveBytes / QuotaBytes * 100.0;

        public async Task OnGetAsync()
        {
            if (!_tenant.IsResolved)
                return;

            QuotaBytes = _quota.GetQuotaBytes(_tenant.TenantId);

            var all = await _db.Set<MediaAsset>()
                .AsNoTracking()
                .Where(m => m.TenantId == _tenant.TenantId)
                .Select(m => new
                {
                    m.Id,
                    m.FileName,
                    m.ContentType,
                    m.SizeBytes,
                    m.StorageKey,
                    m.ThumbStorageKey,
                    m.IsDeleted,
                    m.CreatedAt,
                    m.DeletedAt
                })
                .ToListAsync();

            var active = all.Where(x => !x.IsDeleted).ToList();
            var deleted = all.Where(x => x.IsDeleted).ToList();

            long activeBytes = active.Sum(x => ParseSizeBytes(x.SizeBytes));
            long deletedBytes = deleted.Sum(x => ParseSizeBytes(x.SizeBytes));
            long totalBytes = activeBytes + deletedBytes;

            Summary = new UsageSummary
            {
                ActiveCount = active.Count,
                DeletedCount = deleted.Count,
                TotalCount = all.Count,
                ActiveBytes = activeBytes,
                DeletedBytes = deletedBytes,
                TotalBytes = totalBytes
            };

            // Monthly usage (last 12 months)
            var now = DateTime.UtcNow;
            var start = new DateTime(now.Year, now.Month, 1).AddMonths(-11);

            MonthlyUsage = all
                .Where(x => x.CreatedAt >= start)
                .GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new MonthlyUsageRow
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count(),
                    Bytes = g.Sum(v => ParseSizeBytes(v.SizeBytes))
                })
                .ToList();

            // Content type breakdown (active, top 10)
            ByContentType = active
                .GroupBy(x => string.IsNullOrWhiteSpace(x.ContentType) ? "(unknown)" : x.ContentType.Trim().ToLowerInvariant())
                .Select(g => new ContentTypeRow
                {
                    ContentType = g.Key,
                    Count = g.Count(),
                    Bytes = g.Sum(v => ParseSizeBytes(v.SizeBytes))
                })
                .OrderByDescending(x => x.Bytes)
                .ThenByDescending(x => x.Count)
                .Take(10)
                .ToList();

            LargestActive = active
                .Select(x => new MediaRow
                {
                    Id = x.Id,
                    FileName = x.FileName,
                    ContentType = x.ContentType,
                    Bytes = ParseSizeBytes(x.SizeBytes),
                    StorageKey = x.StorageKey,
                    ThumbStorageKey = x.ThumbStorageKey,
                    CreatedAtUtc = x.CreatedAt
                })
                .OrderByDescending(x => x.Bytes)
                .Take(20)
                .ToList();

            LargestDeleted = deleted
                .Select(x => new MediaRow
                {
                    Id = x.Id,
                    FileName = x.FileName,
                    ContentType = x.ContentType,
                    Bytes = ParseSizeBytes(x.SizeBytes),
                    StorageKey = x.StorageKey,
                    ThumbStorageKey = x.ThumbStorageKey,
                    CreatedAtUtc = x.CreatedAt,
                    DeletedAtUtc = x.DeletedAt
                })
                .OrderByDescending(x => x.Bytes)
                .Take(20)
                .ToList();

            // Cleanup eligibility panel (mirrors HostedService logic)
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
            var retentionDays = Math.Abs(_cleanupOptions.RetentionDays);
            var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);

            var eligible = all
                .Where(x => x.IsDeleted && x.DeletedAt != null && x.DeletedAt <= cutoffUtc)
                .ToList();

            var eligibleBytes = eligible.Sum(x => ParseSizeBytes(x.SizeBytes));

            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

            var runHour = _cleanupOptions.RunHourLocal;
            if (runHour < 0 || runHour > 23) runHour = 2;

            var nextLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, runHour, 0, 0);
            if (nowLocal >= nextLocal)
                nextLocal = nextLocal.AddDays(1);

            Eligibility = new CleanupEligibility
            {
                CleanupEnabled = _cleanupOptions.Enabled,
                RetentionDays = retentionDays,
                RunHourLocal = runHour,
                CutoffUtc = cutoffUtc,
                NextRunLocal = nextLocal,
                EligibleCount = eligible.Count,
                EligibleBytes = eligibleBytes
            };
        }

        // POST: manual cleanup trigger
        public async Task<IActionResult> OnPostRunCleanupNowAsync()
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { success = false, error = "Tenant not resolved." }) { StatusCode = 400 };

            var res = await _cleanupRunner.RunOnceAsync(HttpContext.RequestAborted);

            return new JsonResult(new
            {
                success = true,
                result = res
            });
        }

        private static long ParseSizeBytes(string? sizeBytes)
        {
            if (string.IsNullOrWhiteSpace(sizeBytes))
                return 0;

            var cleaned = sizeBytes.Trim().Replace(",", "");
            return long.TryParse(cleaned, out var v) && v > 0 ? v : 0;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;

            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;
            const long TB = GB * 1024;

            if (bytes >= TB) return $"{bytes / (double)TB:0.##} TB";
            if (bytes >= GB) return $"{bytes / (double)GB:0.##} GB";
            if (bytes >= MB) return $"{bytes / (double)MB:0.##} MB";
            if (bytes >= KB) return $"{bytes / (double)KB:0.##} KB";
            return $"{bytes} B";
        }

        public sealed class UsageSummary
        {
            public int ActiveCount { get; set; }
            public int DeletedCount { get; set; }
            public int TotalCount { get; set; }

            public long ActiveBytes { get; set; }
            public long DeletedBytes { get; set; }
            public long TotalBytes { get; set; }
        }

        public sealed class CleanupEligibility
        {
            public bool CleanupEnabled { get; set; }
            public int RetentionDays { get; set; }
            public int RunHourLocal { get; set; }

            public DateTime CutoffUtc { get; set; }
            public DateTime NextRunLocal { get; set; }

            public int EligibleCount { get; set; }
            public long EligibleBytes { get; set; }
        }

        public sealed class MonthlyUsageRow
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public int Count { get; set; }
            public long Bytes { get; set; }

            public string Label => new DateTime(Year, Month, 1).ToString("yyyy-MM");
        }

        public sealed class ContentTypeRow
        {
            public string ContentType { get; set; } = string.Empty;
            public int Count { get; set; }
            public long Bytes { get; set; }
        }

        public sealed class MediaRow
        {
            public int Id { get; set; }
            public string FileName { get; set; } = string.Empty;
            public string ContentType { get; set; } = string.Empty;
            public long Bytes { get; set; }
            public string StorageKey { get; set; } = string.Empty;
            public string ThumbStorageKey { get; set; } = string.Empty;
            public DateTime CreatedAtUtc { get; set; }
            public DateTime? DeletedAtUtc { get; set; }
        }
    }
}
