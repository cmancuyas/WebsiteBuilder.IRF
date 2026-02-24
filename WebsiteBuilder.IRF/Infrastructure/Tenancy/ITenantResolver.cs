using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public interface ITenantResolver
    {
        Task<ResolvedTenant?> ResolveAsync(string host, string? slug, CancellationToken ct = default);
        Task<ResolvedTenant?> ResolveByIdAsync(Guid tenantId, CancellationToken ct = default);
    }

    public sealed record ResolvedTenant(Guid TenantId, string Slug, string Host);
}
