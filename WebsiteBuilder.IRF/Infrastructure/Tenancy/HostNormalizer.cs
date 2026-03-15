using System.Globalization;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public static class HostNormalizer
    {
        public static string Normalize(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return string.Empty;

            host = host.Trim().TrimEnd('.');

            // Strip port if present (example.com:443)
            var colon = host.IndexOf(':');
            if (colon > 0)
                host = host[..colon];

            host = host.ToLowerInvariant();

            // Convert IDN to ASCII (punycode)
            try
            {
                var idn = new IdnMapping();
                host = idn.GetAscii(host);
            }
            catch
            {
                // If conversion fails, keep original normalized value
            }

            return host;
        }
    }
}