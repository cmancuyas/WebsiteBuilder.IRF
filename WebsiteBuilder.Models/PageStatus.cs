using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PageStatus : BaseModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // Prevent accidental modification of core statuses (Draft/Published/Archived).
        public bool IsSystem { get; set; } = false;

        // Optional but very useful for consistent dropdown ordering
        public int SortOrder { get; set; } = 0;
    }
}
