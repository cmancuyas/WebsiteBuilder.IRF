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
            var normalizedCurrent = NormalizePath(currentPath);

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

                    // NavItem doesn’t include roles in your current design.
                    AllowedRolesCsv = null,

                    IsActive = false,
                    IsAncestorOfActive = false,
                    Children = children
                };
            }

            var mapped = (items.Count > 0 ? items.Select(Map).ToList() : new List<Node>(0));

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
            // External/absolute URLs never become active.
            var nodeUrlNorm = NormalizePath(node.Url);
            var isActive = nodeUrlNorm.Length > 0 && IsMatchOrSection(currentNorm, nodeUrlNorm);

            var anyChildActive = false;
            foreach (var c in node.Children)
                anyChildActive |= MarkActive(c, currentNorm);

            node.IsActive = isActive;
            node.IsAncestorOfActive = anyChildActive;

            return isActive || anyChildActive;
        }

        // ✅ section-aware match:
        // current=/about/team activates node=/about
        private static bool IsMatchOrSection(string currentNorm, string nodeUrlNorm)
        {
            if (nodeUrlNorm == "/")
                return currentNorm == "/";

            if (string.Equals(currentNorm, nodeUrlNorm, System.StringComparison.OrdinalIgnoreCase))
                return true;

            return currentNorm.StartsWith(nodeUrlNorm + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAbsolute(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            return url.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("mailto:", System.StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("tel:", System.StringComparison.OrdinalIgnoreCase);
        }

        // Returns:
        // - "/" for empty
        // - "" for absolute/external (means "never match")
        // - normalized app-relative path for internal links
        private static string NormalizePath(string? pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
                return "/";

            if (IsAbsolute(pathOrUrl))
                return string.Empty;

            // Strip query + hash
            var p = pathOrUrl.Split('?', '#')[0].Trim();
            if (string.IsNullOrWhiteSpace(p))
                return "/";

            if (!p.StartsWith("/", System.StringComparison.Ordinal))
                p = "/" + p;

            // Normalize trailing slash (except root)
            if (p.Length > 1 && p.EndsWith("/", System.StringComparison.Ordinal))
                p = p.TrimEnd('/');

            // Treat /home as /
            if (p.Equals("/home", System.StringComparison.OrdinalIgnoreCase))
                p = "/";

            // keep consistent casing for comparisons
            return p.ToLowerInvariant();
        }
    }
}
