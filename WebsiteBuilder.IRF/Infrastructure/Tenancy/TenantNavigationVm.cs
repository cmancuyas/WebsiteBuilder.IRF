namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantNavigationVm
    {
        public int MenuId { get; init; }
        public string? Variant { get; init; }

        public required string CurrentPath { get; init; }

        // IMPORTANT:
        // Items are *rendering nodes* (enriched with active flags),
        // not the raw service NavItem objects.
        public required IReadOnlyList<Node> Items { get; init; }

        public sealed class Node
        {
            public required string Title { get; init; }
            public required string Url { get; init; }
            public required bool OpenInNewTab { get; init; }

            // Computed after mapping
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

            // Mark active + ancestors
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
            var nodePath = NormalizePath(node.Url);

            // External/absolute URLs never become active.
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

            // remove query + hash
            var p = pathOrUrl.Split('?', '#')[0].Trim();

            if (!p.StartsWith("/")) p = "/" + p;

            // normalize trailing slash (except root)
            if (p.Length > 1 && p.EndsWith("/")) p = p.TrimEnd('/');

            return p.ToLowerInvariant();
        }
    }
}
