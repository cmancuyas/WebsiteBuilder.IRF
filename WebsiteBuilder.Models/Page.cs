using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class Page : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        // URL path segment per tenant, e.g. "about", "listings", "" for home
        [Required, MaxLength(200)]
        public string Slug { get; set; } = string.Empty;

        // Draft/Published/Archived (your PageStatus table or enum)
        public int PageStatusId { get; set; }

        // Optional template key (e.g., "Home", "Landing", "Default")
        [MaxLength(100)]
        public string? LayoutKey { get; set; }

        // SEO
        [MaxLength(200)]
        public string? MetaTitle { get; set; }

        [MaxLength(500)]
        public string? MetaDescription { get; set; }

        // Optional: link to MediaAsset (logo/og image)
        public int? OgImageAssetId { get; set; }

        public DateTime? PublishedAt { get; set; }
        public bool ShowInNavigation { get; set; } = true;
        public int NavigationOrder { get; set; } = 0;

        // ✅ This fixes your Fluent config: WithMany(p => p.Sections)
        public ICollection<PageSection> Sections { get; set; } = new List<PageSection>();

        // Optional (only if you want navigation)
        public Tenant? Tenant { get; set; }

        public int? DraftRevisionId { get; set; }
        public PageRevision? DraftRevision { get; set; }

        public int? PublishedRevisionId { get; set; }
        public PageRevision? PublishedRevision { get; set; }


    }
}
