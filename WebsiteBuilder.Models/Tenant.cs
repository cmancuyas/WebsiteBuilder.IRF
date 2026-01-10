using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class Tenant : BaseModel
    {
        [Key]
        public Guid Id { get; set; }

        [Required, MaxLength(300)]
        public string DisplayName { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Slug { get; set; } = string.Empty;

        public int TenantStatusId { get; set; }

        public int? ActiveThemeId { get; set; }
        public Theme? ActiveTheme { get; set; }

        public DateTime? PublishedAt { get; set; }

        // Owner user lives in Identity DB (separate DB)
        public Guid OwnerUserId { get; set; }

        // Navs (optional but very useful)
        public ICollection<DomainMapping> DomainMappings { get; set; } = new List<DomainMapping>();
        public ICollection<Theme> Themes { get; set; } = new List<Theme>();
        public ICollection<Page> Pages { get; set; } = new List<Page>();
    }
}
