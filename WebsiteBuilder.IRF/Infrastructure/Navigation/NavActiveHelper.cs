using System;

namespace WebsiteBuilder.IRF.Infrastructure.Navigation
{
    public static class NavActiveHelper
    {
        public static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "/";

            var p = path.Trim();

            // Strip query if someone passes full URL-ish strings
            var q = p.IndexOf('?', StringComparison.Ordinal);
            if (q >= 0) p = p[..q];

            // Ensure leading slash for internal paths
            if (!p.StartsWith("/", StringComparison.Ordinal)) p = "/" + p;

            // Normalize trailing slash (except root)
            if (p.Length > 1 && p.EndsWith("/", StringComparison.Ordinal))
                p = p.TrimEnd('/');

            return p.ToLowerInvariant();
        }

        // "Active" means: exact match OR current is inside that section (/docs/... should activate /docs)
        public static bool IsActive(string currentPath, string itemUrl)
        {
            var cur = NormalizePath(currentPath);
            var url = NormalizePath(itemUrl);

            if (url == "/") return cur == "/";

            if (cur == url) return true;
            return cur.StartsWith(url + "/", StringComparison.Ordinal);
        }

        public static bool HasActiveDescendant(string currentPath, IReadOnlyList<string> descendantUrls)
        {
            foreach (var u in descendantUrls)
            {
                if (IsActive(currentPath, u)) return true;
            }
            return false;
        }
    }
}
