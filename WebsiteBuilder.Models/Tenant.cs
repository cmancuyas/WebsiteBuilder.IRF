using System;
using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.Models
{
    public class Tenant : BaseModel
    {
        [Key]
        public Guid Id { get; set; }

        // Public/business display name for the agent site
        [Required, MaxLength(150)]
        public string DisplayName { get; set; } = string.Empty;

        // Used for subdomain routing: {slug}.yourplatform.com
        // Must be unique across tenants.
        [Required, MaxLength(100)]
        public string Slug { get; set; } = string.Empty;

        // Trial / Active / Suspended / Cancelled
        [Required]
        public int TenantStatusId { get; set; }

        // The currently active theme for rendering the site
        public int? ActiveThemeId { get; set; }

        // Set when the tenant site is first published (optional)
        public DateTime? PublishedAt { get; set; }

        // Identity user who owns this tenant
        [Required]
        public Guid OwnerUserId { get; set; }

        // Navigation (optional, but recommended)
        public TenantStatus? TenantStatus { get; set; }
        public Theme? ActiveTheme { get; set; }
    }
}
