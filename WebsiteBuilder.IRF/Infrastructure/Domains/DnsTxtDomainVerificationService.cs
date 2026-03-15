using DnsClient;

namespace WebsiteBuilder.IRF.Infrastructure.Domains
{
    public sealed class DnsTxtDomainVerificationService : IDomainVerificationService
    {
        private readonly LookupClient _dns = new();

        public async Task<(bool ok, string? error)> VerifyDnsTxtAsync(string normalizedHost, string token, CancellationToken ct)
        {
            var verifyHost = $"_wb-verify.{normalizedHost}";
            var expected = $"wb={token}";

            try
            {
                var result = await _dns.QueryAsync(verifyHost, QueryType.TXT, cancellationToken: ct);

                foreach (var r in result.Answers.TxtRecords())
                {
                    var val = string.Join("", r.Text);
                    if (string.Equals(val, expected, StringComparison.Ordinal))
                        return (true, null);
                }

                return (false, $"TXT record not found/matching at {verifyHost}. Expected exact value: {expected}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}