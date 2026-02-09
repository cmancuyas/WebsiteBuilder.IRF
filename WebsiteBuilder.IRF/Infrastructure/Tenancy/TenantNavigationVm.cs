namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantNavigationVm
    {
        public int MenuId { get; init; }
        public string? Variant { get; init; }

        public required string CurrentPath { get; init; }

        // Enriched nodes (active flags computed per request)
        public required IReadOnlyList<Node> Items { get; init; }

        public sealed class Node
        {
            public required string Title { get; init; }
            public required string Url { get; init; }
            public required bool OpenInNewTab { get; init; }

            // Optional (kept for future use; not required by views)
            public string? AllowedRolesCsv { get; init; }

            // Per-request flags
            public bool IsActive { get; set; }
            public bool IsAncestorOfActive { get; set; }

            public required IReadOnlyList<Node> Children { get; init; }
        }

        public static TenantNavigationVm From(
            IReadOnlyList<NavItem> items,
            string currentPath,
            int menuId,
            string? variant)
        {
            var normalizedCurrent = NavigationPath.NormalizePath(currentPath);

            static Node Map(NavItem x)
            {
                var children = x.Children.Count > 0
                    ? x.Children.Select(Map).ToList()
                    : new List<Node>(0);

                return new Node
                {
                    Title = x.Title,
                    Url = x.Url,
                    OpenInNewTab = x.OpenInNewTab,
                    AllowedRolesCsv = null,

                    IsActive = false,
                    IsAncestorOfActive = false,
                    Children = children
                };
            }

            var mapped = items.Count > 0 ? items.Select(Map).ToList() : new List<Node>(0);

            foreach (var n in mapped)
                MarkActive(n, normalizedCurrent);

            return new TenantNavigationVm
            {
                MenuId = menuId,
                Variant = variant,
                Items = mapped,
                CurrentPath = normalizedCurrent
            };
        }

        private static bool MarkActive(Node node, string currentNorm)
        {
            var (isActive, isAncestorByUrl) = NavigationPath.GetActiveFlags(node.Url, currentNorm);

            var anyChildActive = false;
            foreach (var c in node.Children)
                anyChildActive |= MarkActive(c, currentNorm);

            node.IsActive = isActive;
            node.IsAncestorOfActive = isAncestorByUrl || anyChildActive;

            return node.IsActive || node.IsAncestorOfActive;
        }
    }
}
