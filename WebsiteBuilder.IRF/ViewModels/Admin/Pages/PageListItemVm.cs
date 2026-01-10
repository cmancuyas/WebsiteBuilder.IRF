namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageListItemVm
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int PageStatusId { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
