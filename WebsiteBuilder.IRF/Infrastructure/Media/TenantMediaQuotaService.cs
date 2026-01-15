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

    public async Task<(bool allowed, long usedBytes, long quotaBytes)> CanUploadAsync(Guid tenantId, long newBytes, CancellationToken ct)
    {
        // SizeBytes is stored as string in your model; parse defensively.
        var sizeStrings = await _db.Set<MediaAsset>()
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && !m.IsDeleted)
            .Select(m => m.SizeBytes)
            .ToListAsync(ct);

        long used = 0;
        foreach (var s in sizeStrings)
        {
            if (long.TryParse(s, out var v) && v > 0)
                used += v;
        }

        var quota = _opts.DefaultQuotaBytes;
        var allowed = used + Math.Max(0, newBytes) <= quota;

        return (allowed, used, quota);
    }
    public long GetQuotaBytes(Guid tenantId)
    {
        if (_opts.TenantQuotaBytes.TryGetValue(tenantId, out var quota) && quota > 0)
            return quota;

        return _opts.DefaultQuotaBytes > 0 ? _opts.DefaultQuotaBytes : 0;
    }
}
