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

        // Snapshot fields (adjust names to match your PageSection model)
        [MaxLength(100)]
        public string TypeKey { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public string? ContentJson { get; set; }
        public string? SettingsJson { get; set; }
    }
}
