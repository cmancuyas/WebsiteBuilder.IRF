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
        public Guid OwnerUserId { get; set; }

        // Draft/Published/Archived
        public int PageStatusId { get; set; }

        [MaxLength(100)]
        public string? LayoutKey { get; set; }

        [MaxLength(200)]
        public string? MetaTitle { get; set; }

        [MaxLength(500)]
        public string? MetaDescription { get; set; }

        public int? OgImageAssetId { get; set; }

        public DateTime? PublishedAt { get; set; }

        public bool ShowInNavigation { get; set; } = true;
        public int NavigationOrder { get; set; } = 0;

        // Optional (only if you want navigation)
        public Tenant? Tenant { get; set; }

        // Draft revision pointer
        public int? DraftRevisionId { get; set; }
        public PageRevision? DraftRevision { get; set; }

        // Published revision pointer
        public int? PublishedRevisionId { get; set; }
        public PageRevision? PublishedRevision { get; set; }
    }
}
