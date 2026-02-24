using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Rendering;

public sealed class PageRenderContext
{
    public required Page PageEntity { get; init; }
    public required bool IsPreview { get; init; }

    public string? CanonicalUrl { get; init; }
    public bool RobotsNoIndex { get; init; }
    public required IReadOnlyList<RenderSectionDto> RenderSections { get; init; }
    public string? RedirectToUrl { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? OgImageUrl { get; set; }

    public sealed class RenderSectionDto
    {
        public int SectionTypeId { get; init; }
        public string? SectionTypeName { get; init; }
        public int SortOrder { get; init; }
        public string? SettingsJson { get; init; }
        public string? SectionTypeKey { get; init; }
    }
}
