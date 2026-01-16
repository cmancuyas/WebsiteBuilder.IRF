namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class SectionRowVm
    {
        public int Id { get; set; }
        public int SectionTypeId { get; set; }
        public int SortOrder { get; set; }
        public string? SettingsJson { get; set; }
    }
}
