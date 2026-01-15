using System;
using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class MediaAsset : BaseModel
    {
        [Key]
        public int Id { get; set; }

        // NEW: tenant scoping (Guid matches ITenantContext usage in IRF Razor Pages)
        public Guid TenantId { get; set; }

        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(255)]
        public string ContentType { get; set; } = string.Empty;

        [MaxLength(255)]
        public string SizeBytes { get; set; } = string.Empty;

        // Public URL path (e.g. /uploads/2026/01/abc.jpg)
        [MaxLength(500)]
        public string StorageKey { get; set; } = string.Empty;

        // NEW: thumbnail URL path (e.g. /uploads/thumbs/2026/01/abc.webp)
        [MaxLength(500)]
        public string ThumbStorageKey { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Width { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Height { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string AltText { get; set; } = string.Empty;

        [MaxLength(64)]
        public string CheckSum { get; set; } = string.Empty;
    }
}
