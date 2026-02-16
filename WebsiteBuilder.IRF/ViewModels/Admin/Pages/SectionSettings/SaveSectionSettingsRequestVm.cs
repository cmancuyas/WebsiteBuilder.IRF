namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages.SectionSettings
{
    public sealed class SaveSectionSettingsRequestVm
    {
        public int SectionId { get; set; }

        // base64 concurrency tokens
        public string? DraftRevisionRowVersion { get; set; }
        public string? SectionRowVersion { get; set; }

        public string SettingsJson { get; set; } = "{}";
    }
}
