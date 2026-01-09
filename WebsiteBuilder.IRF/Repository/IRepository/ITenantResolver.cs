using System.Threading;
using System.Threading.Tasks;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Repository.IRepository
{
    public interface ITenantResolver
    {
        Task<TenantResolutionResult?> ResolveAsync(string host, string? slug, CancellationToken cancellationToken);
    }
}
