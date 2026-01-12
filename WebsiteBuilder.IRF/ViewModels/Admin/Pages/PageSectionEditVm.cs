using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageSectionEditVm
    {
        [Required]
        public int Id { get; set; }

        // Canonical discriminator
        [Required]
        public int SectionTypeId { get; set; }

        // Canonical payload
        public string? SettingsJson { get; set; }
    }
}
