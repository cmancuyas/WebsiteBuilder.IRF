namespace WebsiteBuilder.IRF.Infrastructure.Sections.Settings;

public sealed class GallerySettings
{
    public List<int> ImageAssetIds { get; set; } = new();
    public string? Layout { get; set; } // "grid" | "carousel"
}
