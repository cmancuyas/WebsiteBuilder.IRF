using System.Globalization;

namespace WebsiteBuilder.IRF.Infrastructure.Domains
{
    public static class DomainHostNormalizer
    {
        public static string Normalize(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return string.Empty;

            host = host.Trim().TrimEnd('.').ToLowerInvariant();

            var idn = new IdnMapping();
            return idn.GetAscii(host);
        }
    }
}