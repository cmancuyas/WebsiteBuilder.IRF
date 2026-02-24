using System;
using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public sealed class PageSlugHistory : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        public int PageId { get; set; }
        public Page? Page { get; set; }

        [Required, MaxLength(200)]
        public string OldSlug { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? NewSlug { get; set; } // optional but useful for diagnostics

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        public Guid? ChangedByUserId { get; set; }
    }
}
