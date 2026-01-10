namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageSectionListItemVm
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
        public string TypeKey { get; set; } = string.Empty;
        public string? ContentJson { get; set; }
    }
}
