using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageSectionEditVm
    {
        [Required]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string TypeKey { get; set; } = string.Empty;

        public string? ContentJson { get; set; }
    }
}
