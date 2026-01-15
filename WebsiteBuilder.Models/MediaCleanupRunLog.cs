using System;
using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public sealed class MediaCleanupRunLog : BaseModel
    {
        [Key]
        public long Id { get; set; }

        // Tenant scope (multi-tenant)
        public Guid TenantId { get; set; }

        // "Nightly", "Manual", "OnDemand", etc.
        [Required]
        [MaxLength(50)]
        public string RunType { get; set; } = "Nightly";

        // Configuration used for the run (self-describing)
        public int RetentionDays { get; set; }
        public int BatchSize { get; set; }

        // Run timing (UTC)
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAtUtc { get; set; }

        // Counters
        public int EligibleCount { get; set; }                  // eligible when scanning
        public int ProcessedCount { get; set; }                 // attempted
        public int DeletedOriginalFilesCount { get; set; }
        public int DeletedThumbnailFilesCount { get; set; }
        public int HardDeletedDbRowsCount { get; set; }         // DB rows removed
        public int FailedCount { get; set; }

        // Status and diagnostics
        [Required]
        [MaxLength(30)]
        public string Status { get; set; } = "Running";         // Running | Succeeded | Failed | Partial

        [MaxLength(2000)]
        public string? ErrorSummary { get; set; }

        // Optional notes (keep short)
        [MaxLength(4000)]
        public string? Notes { get; set; }
    }
}
