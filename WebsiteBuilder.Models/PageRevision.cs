using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PageRevision : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        // Parent page
        public int PageId { get; set; }
        public Page? Page { get; set; }

        // Snapshot metadata
        public int VersionNumber { get; set; }            // monotonically increasing per Page
        public bool IsPublishedSnapshot { get; set; }     // always true for publish snapshots

        // Page fields snapshot
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(255)]
        public string Slug { get; set; } = string.Empty;

        [MaxLength(255)]
        public string LayoutKey { get; set; } = string.Empty;

        [MaxLength(255)]
        public string MetaTitle { get; set; } = string.Empty;

        [MaxLength(500)]
        public string MetaDescription { get; set; } = string.Empty;

        public int? OgImageAssetId { get; set; }
        public DateTime? PublishedAt { get; set; }

        public ICollection<PageRevisionSection> Sections { get; set; } = new List<PageRevisionSection>();
    }
}
