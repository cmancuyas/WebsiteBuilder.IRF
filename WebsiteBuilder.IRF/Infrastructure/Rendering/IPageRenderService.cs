using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Rendering;

public interface IPageRenderService
{
    Task<string> RenderRevisionSectionsAsync(PageRevision revision, CancellationToken ct = default);
}
