namespace WebsiteBuilder.IRF.ViewModels.Admin.Navigation
{
    public sealed class NavSaveRequest
    {
        public int MenuId { get; set; }
        public List<NavSaveItemDto> Items { get; set; } = new();
    }

    public sealed class NavSaveItemDto
    {
        // Existing DB IDs are positive.
        // Client-side new items use negative IDs.
        public int Id { get; set; }

        public int? ParentId { get; set; }
        public int SortOrder { get; set; }

        public string Label { get; set; } = string.Empty;

        public int? PageId { get; set; }
        public string Url { get; set; } = string.Empty;

        public bool OpenInNewTab { get; set; }
        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }
    }

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

        public List<PageOptionVm> PageOptions { get; set; } = new();
        public List<NavNodeVm> Children { get; set; } = new();
    }

    public sealed class PageOptionVm
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }
}
