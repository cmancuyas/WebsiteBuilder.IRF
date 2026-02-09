using Microsoft.AspNetCore.Http;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy;

public static class NavigationPath
{
    private static bool IsAbsoluteUrl(string s)
    {
        // treat these as external/non-route
        return s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizePath(string? path)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return "/";

        // disabled placeholder: never match anything
        if (p.Trim() == "#") return string.Empty;
        
        // external/absolute: never match anything
        if (IsAbsoluteUrl(p)) return string.Empty;

        // strip query/hash
        var q = p.IndexOfAny(['?', '#']);
        if (q >= 0) p = p[..q];

        if (string.IsNullOrWhiteSpace(p)) return "/";

        if (!p.StartsWith("/")) p = "/" + p;

        // canonical home
        if (p.Equals("/home", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("/index", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
            return "/";

        if (p.Length > 1 && p.EndsWith("/"))
            p = p.TrimEnd('/');

        return p;
    }

    public static (bool isActive, bool isAncestor) GetActiveFlags(string? itemUrl, string? currentPath)
    {
        // Never match disabled placeholders
        if (string.IsNullOrWhiteSpace(itemUrl) || itemUrl.Trim() == "#")
            return (false, false);

        var item = NormalizePath(itemUrl);
        var cur = NormalizePath(currentPath);

        // If item normalized to "" (e.g., "#"), never match
        if (string.IsNullOrEmpty(item)) return (false, false);

        // exact match
        if (string.Equals(item, cur, StringComparison.OrdinalIgnoreCase))
            return (true, false);

        // root shouldn't be ancestor of everything
        if (item == "/")
            return (false, false);

        // segment-safe ancestor
        return (false, cur.StartsWith(item + "/", StringComparison.OrdinalIgnoreCase));
    }

    public static string CurrentPath(HttpContext? ctx)
        => NormalizePath(ctx?.Request?.Path.Value ?? "/");
    public static bool IsActiveOrAncestor(string? itemUrl, string? currentPath)
    {
        var (isActive, isAncestor) = GetActiveFlags(itemUrl, currentPath);
        return isActive || isAncestor;
    }

}
