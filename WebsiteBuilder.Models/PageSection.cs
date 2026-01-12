using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PageSection : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        // TenantBaseModel provides:
        // TenantId, IsActive, IsDeleted, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy

        [Required]
        public int PageId { get; set; }

        public Page? Page { get; set; }

        // Deterministic rendering order
        public int SortOrder { get; set; } = 0;

        // Canonical discriminator (FK → SectionTypes)
        [Required]
        public int SectionTypeId { get; set; }

        public SectionType? SectionType { get; set; }

        // Optional: label for admin UI
        [MaxLength(200)]
        public string? DisplayName { get; set; }

        // Canonical JSON payload
        // Must be valid JSON object (validated at service layer)
        public string? SettingsJson { get; set; }
    }
}
