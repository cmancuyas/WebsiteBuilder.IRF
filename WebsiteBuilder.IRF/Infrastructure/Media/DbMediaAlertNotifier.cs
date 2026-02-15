using System.Text.RegularExpressions;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class DbMediaAlertNotifier : IMediaAlertNotifier
{
    private static readonly Regex RunIdRegex =
        new(@"(?:^|[,\s])RunId=(\d+)(?:$|[,\s])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly DataContext _db;
    private readonly ITenantContext _tenant;

    public DbMediaAlertNotifier(DataContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task NotifyAsync(string subject, string message, CancellationToken ct)
    {
        // Try to extract RunId=123 from the message (your runner emits this format)
        long? runId = null;
        var m = RunIdRegex.Match(message ?? string.Empty);
        if (m.Success && long.TryParse(m.Groups[1].Value, out var id) && id > 0)
            runId = id;

        var alert = new MediaAlert
        {
            // If you don’t have tenant scoping for alerts yet, you can set TenantId later.
            // For now: store Guid.Empty (or if you have tenant context available, inject it and use real tenantId).
            TenantId = _tenant.TenantId,

            Subject = (subject ?? string.Empty).Trim(),
            Message = (message ?? string.Empty).Trim(),
            Severity = "Warning",

            MediaCleanupRunLogId = runId,

            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.Empty // replace if you have a system user id convention
        };

        _db.MediaAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);
    }
}
