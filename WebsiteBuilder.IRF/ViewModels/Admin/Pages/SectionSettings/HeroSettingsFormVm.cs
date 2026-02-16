namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages.SectionSettings
{
    public sealed class HeroSettingsFormVm
    {
        public int SectionId { get; set; }
        public string DraftRevisionRowVersionBase64 { get; set; } = "";
        public string SectionRowVersionBase64 { get; set; } = "";

        public HeroSectionSettingsVm Settings { get; set; } = new();
    }
}
