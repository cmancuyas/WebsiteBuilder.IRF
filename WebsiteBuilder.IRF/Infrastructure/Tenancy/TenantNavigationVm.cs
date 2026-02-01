namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantNavigationVm
    {
        public required IReadOnlyList<Node> Items { get; init; }
        public required string CurrentPath { get; init; }

        public sealed class Node
        {
            public required string Title { get; init; }
            public required string Url { get; init; }
            public required bool OpenInNewTab { get; init; }

            // These are computed after mapping
            public required bool IsActive { get; set; }
            public required bool IsAncestorOfActive { get; set; }

            public required IReadOnlyList<Node> Children { get; init; }
        }

        public static TenantNavigationVm From(IReadOnlyList<NavItem> items, string currentPath)
        {
            var normalizedCurrent = NormalizePath(currentPath);

            Node Map(NavItem x) => new()
            {
                Title = x.Title,
                Url = x.Url,
                OpenInNewTab = x.OpenInNewTab,
                IsActive = false,               // ✅ required member set
                IsAncestorOfActive = false,     // ✅ required member set
                Children = x.Children.Select(Map).ToList()
            };

            var mapped = items.Select(Map).ToList();

            // Mark active + ancestors
            foreach (var n in mapped)
                MarkActive(n, normalizedCurrent);

            return new TenantNavigationVm
            {
                Items = mapped,
                CurrentPath = normalizedCurrent
            };
        }

        private static bool MarkActive(Node node, string current)
        {
            var nodePath = NormalizePath(node.Url);

            var isExact = !IsAbsolute(node.Url) && nodePath == current;
            var anyChildActive = node.Children.Any(c => MarkActive(c, current));

            node.IsActive = isExact;
            node.IsAncestorOfActive = anyChildActive;

            return node.IsActive || node.IsAncestorOfActive;
        }

        private static bool IsAbsolute(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out _);

        private static string NormalizePath(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl)) return "/";

            // Absolute => ignore for active matching
            if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _)) return "";

            var p = pathOrUrl.Split('?', '#')[0].Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            if (p.Length > 1 && p.EndsWith("/")) p = p.TrimEnd('/');
            return p.ToLowerInvariant();
        }
    }
}
