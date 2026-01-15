using System;
using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class MediaAsset : BaseModel
    {
        [Key]
        public int Id { get; set; }

        // Tenant scoping
        public Guid TenantId { get; set; }

        [Required, MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string ContentType { get; set; } = string.Empty;

        // Size in bytes (for quotas / cleanup / analytics)
        public long SizeBytes { get; set; }

        // REQUIRED: primary storage pointer
        [Required, MaxLength(500)]
        public string StorageKey { get; set; } = string.Empty;

        // Optional thumbnail pointer
        [MaxLength(500)]
        public string? ThumbStorageKey { get; set; }

        // Image dimensions (nullable for non-images)
        public int? Width { get; set; }
        public int? Height { get; set; }

        [MaxLength(1000)]
        public string? AltText { get; set; }

        // Content hash (deduplication / integrity)
        [Required, MaxLength(64)]
        public string CheckSum { get; set; } = string.Empty;
    }
}
