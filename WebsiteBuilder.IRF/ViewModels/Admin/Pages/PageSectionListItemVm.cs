namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageSectionListItemVm
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }

        public int SectionTypeId { get; set; }
        public string SectionTypeName { get; set; } = string.Empty;

        // Used to populate the editor textarea in the UI
        public string? SettingsJson { get; set; }
    }
}
