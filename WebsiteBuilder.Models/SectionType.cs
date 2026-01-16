using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class SectionType : BaseModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // Optional (recommended if you use it elsewhere already):
        public int SortOrder { get; set; }
        public string Key { get; set; } = string.Empty; // e.g., "Hero", "Text", "Gallery"
    }
}
