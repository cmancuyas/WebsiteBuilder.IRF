namespace WebsiteBuilder.IRF.Infrastructure.Rendering;

public sealed class CachedPublishedRenderData
{
    public required int PageId { get; init; }
    public required int PublishedRevisionId { get; init; }

    public string? RevisionTitle { get; init; }
    public string? RevisionSlug { get; init; }

    public required IReadOnlyList<PageRenderContext.RenderSectionDto> Sections { get; init; }
}
