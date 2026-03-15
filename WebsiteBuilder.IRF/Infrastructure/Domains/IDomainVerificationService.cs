namespace WebsiteBuilder.IRF.Infrastructure.Domains
{
    public interface IDomainVerificationService
    {
        Task<(bool ok, string? error)> VerifyDnsTxtAsync(string normalizedHost, string token, CancellationToken ct);
    }
}