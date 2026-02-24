namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public static class HostNormalizer
    {
        public static string Normalize(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return string.Empty;

            host = host.Trim().TrimEnd('.');

            // Strip port if present (e.g., example.com:443)
            var colon = host.IndexOf(':');
            if (colon > 0) host = host[..colon];

            return host.ToLowerInvariant();
        }
    }
}