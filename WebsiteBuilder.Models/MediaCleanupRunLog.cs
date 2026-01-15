using System;
using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public sealed class MediaCleanupRunLog : BaseModel
    {
        [Key]
        public long Id { get; set; }

        // Scope (tenant-aware). Keep consistent with other models using int TenantId.
        public int TenantId { get; set; }

        // "Nightly", "Manual", "OnDemand", etc.
        [MaxLength(50)]
        public string RunType { get; set; } = "Nightly";

        // The configuration used for the run (so the log is self-describing)
        public int RetentionDays { get; set; }
        public int BatchSize { get; set; }

        // Run timing (UTC, always)
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAtUtc { get; set; }

        // Counters (what we planned to do vs. what we did)
        public int EligibleCount { get; set; }          // how many assets were eligible when scanning
        public int ProcessedCount { get; set; }         // how many attempted
        public int DeletedOriginalFilesCount { get; set; }
        public int DeletedThumbnailFilesCount { get; set; }
        public int HardDeletedDbRowsCount { get; set; } // how many DB rows removed (if you hard-delete rows)
        public int FailedCount { get; set; }

        // Status and diagnostics
        [MaxLength(30)]
        public string Status { get; set; } = "Running"; // Running | Succeeded | Failed | Partial

        [MaxLength(2000)]
        public string? ErrorSummary { get; set; }

        // Optional: store a sample of failing storage keys or IDs (keep short; don’t log sensitive data)
        [MaxLength(4000)]
        public string? Notes { get; set; }
    }
}
