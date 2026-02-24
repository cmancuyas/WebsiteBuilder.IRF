using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages.SectionSettings
{
    public sealed class HeroSectionSettingsVm
    {
        [Required, StringLength(80)]
        public string Headline { get; set; } = "";

        [StringLength(200)]
        public string? Subheadline { get; set; }

        [Url]
        public string? BackgroundImageUrl { get; set; }

        [StringLength(40)]
        public string? PrimaryCtaText { get; set; }

        [Url]
        public string? PrimaryCtaUrl { get; set; }

        [RegularExpression("^(left|center|right)$")]
        public string Alignment { get; set; } = "left";
    }
}
