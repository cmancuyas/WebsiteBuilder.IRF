namespace WebsiteBuilder.IRF.Infrastructure.Media;

public interface ITenantMediaQuotaService
{
    Task<(bool allowed, long usedBytes, long quotaBytes)> CanUploadAsync(Guid tenantId, long newBytes, CancellationToken ct);
    long GetQuotaBytes(Guid tenantId);
}
