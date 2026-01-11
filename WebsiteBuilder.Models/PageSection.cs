using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PageSection : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        // TenantBaseModel should already carry TenantId, IsActive, IsDeleted, audit fields, etc.

        [Required]
        public int PageId { get; set; }

        public Page? Page { get; set; }

        // Deterministic rendering order
        public int SortOrder { get; set; } = 0;

        // Stable type discriminator for renderer (Hero, Text, Gallery, CTA, etc.)
        [Required]
        [MaxLength(100)]
        public string TypeKey { get; set; } = "Text";

        // Optional: allow per-section title/label (useful for admin UI)
        [MaxLength(200)]
        public string? DisplayName { get; set; }

        // JSON payload (use this for both content + settings if you want to keep it simple)
        public string? ContentJson { get; set; }

        // Optional: future-proofing—if you want separate “settings” without breaking existing rows
        public string? SettingsJson { get; set; }
    }
}
