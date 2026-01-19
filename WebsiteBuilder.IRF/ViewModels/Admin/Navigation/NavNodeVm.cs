namespace WebsiteBuilder.IRF.ViewModels.Admin.Navigation
{
    public sealed class NavNodeVm
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }

        public string Label { get; set; } = string.Empty;

        public int? PageId { get; set; }
        public string Url { get; set; } = string.Empty;

        public bool OpenInNewTab { get; set; }
        public bool IsActive { get; set; }

        public bool IsPublished { get; set; } = true;
        public string? AllowedRolesCsv { get; set; }

        public List<PageOptionVm> PageOptions { get; set; } = new();
        public List<NavNodeVm> Children { get; set; } = new();
    }
}
