using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PageSection : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        public int PageId { get; set; }
        public Page? Page { get; set; }

        public int SortOrder { get; set; } = 0;

        // Simple generic payload; adjust as you like
        [MaxLength(100)]
        public string TypeKey { get; set; } = "Text";

        public string? ContentJson { get; set; }
    }
}
