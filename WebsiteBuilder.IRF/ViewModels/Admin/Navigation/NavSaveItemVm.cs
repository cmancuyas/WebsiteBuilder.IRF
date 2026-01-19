namespace WebsiteBuilder.IRF.ViewModels.Admin.Navigation
{
    public sealed class NavSaveItemVm
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

        // Publishing & visibility
        public bool IsPublished { get; set; } = true;

        // CSV list of roles allowed to see this nav item.
        // Null/empty => visible to everyone.
        public string? AllowedRolesCsv { get; set; }

        // Explicit restore intent (no auto-undelete)
        public bool Restore { get; set; }
    }
}
