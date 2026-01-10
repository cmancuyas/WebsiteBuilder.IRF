using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class Theme : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Mode { get; set; } = "light"; // light/dark

        // JSON config / CSS variables
        public string? ThemeJson { get; set; }

        public Tenant? Tenant { get; set; }
    }
}
