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

        public int SortOrder { get; set; }

        [Required, MaxLength(100)]
        public string Key { get; set; } = string.Empty; // e.g. "Hero", "Text", "Gallery"
    }
}
