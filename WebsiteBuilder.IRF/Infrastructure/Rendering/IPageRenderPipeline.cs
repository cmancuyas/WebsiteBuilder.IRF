namespace WebsiteBuilder.IRF.Infrastructure.Rendering;

public interface IPageRenderPipeline
{
    Task<PageRenderContext?> BuildForSlugAsync(string? slug, bool previewRequested, CancellationToken ct = default);
    Task<PageRenderContext?> BuildDraftForPageIdAsync(int pageId, CancellationToken ct = default);
}
