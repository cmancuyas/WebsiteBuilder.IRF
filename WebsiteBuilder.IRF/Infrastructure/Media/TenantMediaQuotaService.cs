using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class TenantMediaQuotaService : ITenantMediaQuotaService
{
    private readonly DataContext _db;
    private readonly MediaQuotaOptions _opts;

    public TenantMediaQuotaService(DataContext db, IOptions<MediaQuotaOptions> opts)
    {
        _db = db;
        _opts = opts.Value;
    }

    public async Task<(bool allowed, long usedBytes, long quotaBytes)> CanUploadAsync(
        Guid tenantId,
        long newBytes,
        CancellationToken ct)
    {
        // Sum happens in SQL (efficient). Cast to nullable to avoid null-sum issues.
        var usedNullable = await _db.Set<MediaAsset>()
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && !m.IsDeleted)
            .SumAsync(m => (long?)m.SizeBytes, ct);

        var used = usedNullable ?? 0L;

        var quota = _opts.DefaultQuotaBytes;

        // Defensive: treat negative as 0
        var incoming = Math.Max(0L, newBytes);

        var allowed = used + incoming <= quota;

        return (allowed, used, quota);
    }

    public long GetQuotaBytes(Guid tenantId)
    {
        if (_opts.TenantQuotaBytes.TryGetValue(tenantId, out var quota) && quota > 0)
            return quota;

        return _opts.DefaultQuotaBytes > 0 ? _opts.DefaultQuotaBytes : 0;
    }
}
