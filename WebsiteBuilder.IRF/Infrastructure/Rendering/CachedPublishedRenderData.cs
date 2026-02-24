namespace WebsiteBuilder.IRF.Infrastructure.Rendering;

public sealed class CachedPublishedRenderData
{
    public required int PageId { get; init; }
    public required int PublishedRevisionId { get; init; }

    public string? RevisionTitle { get; init; }
    public string? RevisionSlug { get; init; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public int? OgImageAssetId { get; set; }

    public IReadOnlyList<PageRenderContext.RenderSectionDto> Sections { get; set; }
        = Array.Empty<PageRenderContext.RenderSectionDto>();
}
