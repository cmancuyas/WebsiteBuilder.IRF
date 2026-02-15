namespace WebsiteBuilder.IRF.Infrastructure.Pages;

public static class SlugUtil
{
    public static string Normalize(string? raw)
    {
        var s = (raw ?? "").Trim();

        // home allowed via "" or "/"
        if (s == "/" || s.Equals("home", StringComparison.OrdinalIgnoreCase))
            return "";

        // strip leading/trailing slashes
        s = s.Trim('/');

        // collapse multiple slashes inside
        while (s.Contains("//", StringComparison.Ordinal))
            s = s.Replace("//", "/", StringComparison.Ordinal);

        // lowercase
        s = s.ToLowerInvariant();

        return s;
    }
}
