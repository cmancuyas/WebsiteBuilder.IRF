using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PageRevisionSection : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        public int PageRevisionId { get; set; }
        public PageRevision? PageRevision { get; set; }

        // Optional: reference to the live section row at the time of snapshot
        public int? SourcePageSectionId { get; set; }

        // ✅ Align with PageSection
        public int SectionTypeId { get; set; }
        public SectionType? SectionType { get; set; }

        // ✅ Align with PageSection (your live model uses string)
        public int SortOrder { get; set; } = 0;

        // ✅ Snapshot payload
        public string? SettingsJson { get; set; }
    }
}
