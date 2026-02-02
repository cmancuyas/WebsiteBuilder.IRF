namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantNavigationVm
    {
        public int MenuId { get; init; }
        public string? Variant { get; init; }

        public required string CurrentPath { get; init; }

        // Rendering nodes (enriched with active flags), not raw NavItem objects.
        public required IReadOnlyList<Node> Items { get; init; }

        public sealed class Node
        {
            public required string Title { get; init; }
            public required string Url { get; init; }
            public required bool OpenInNewTab { get; init; }

            public required bool IsActive { get; set; }
            public required bool IsAncestorOfActive { get; set; }

            public required IReadOnlyList<Node> Children { get; init; }
        }

        public static TenantNavigationVm From(
            IReadOnlyList<NavItem> items,
            string currentPath,
            int menuId,
            string? variant)
        {
            var normalizedCurrent = NormalizePath(currentPath);

            Node Map(NavItem x) => new()
            {
                Title = x.Title,
                Url = x.Url,
                OpenInNewTab = x.OpenInNewTab,
                IsActive = false,
                IsAncestorOfActive = false,
                Children = x.Children.Select(Map).ToList()
            };

            var mapped = items.Select(Map).ToList();

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

        private static bool MarkActive(Node node, string current)
        {
            // External/absolute URLs never become active.
            var isExact = !IsAbsolute(node.Url) && NormalizePath(node.Url) == current;

            var anyChildActive = node.Children.Any(c => MarkActive(c, current));

            node.IsActive = isExact;
            node.IsAncestorOfActive = anyChildActive;

            return node.IsActive || node.IsAncestorOfActive;
        }

        private static bool IsAbsolute(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out _);

        private static string NormalizePath(string? pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
                return "/";

            // Absolute URL => never match current path
            if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _))
                return "#abs";

            // Strip query + hash
            var p = pathOrUrl.Split('?', '#')[0].Trim();

            if (string.IsNullOrWhiteSpace(p))
                return "/";

            if (!p.StartsWith("/"))
                p = "/" + p;

            // Normalize trailing slash (except root)
            if (p.Length > 1 && p.EndsWith("/"))
                p = p.TrimEnd('/');

            // Treat /home as /
            if (p.Equals("/home", System.StringComparison.OrdinalIgnoreCase))
                p = "/";

            return p.ToLowerInvariant();
        }
    }
}
