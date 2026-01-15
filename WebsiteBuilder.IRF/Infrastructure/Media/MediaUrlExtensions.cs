using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Media;

public static class MediaUrlExtensions
{
    public static string MediaThumb(this IUrlHelper url, MediaAsset? asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.ThumbStorageKey))
            return url.Content("~/img/placeholder.png");

        return url.Content(asset.ThumbStorageKey);
    }

    public static string MediaFile(this IUrlHelper url, MediaAsset? asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.StorageKey))
            return "#";

        return url.Content(asset.StorageKey);
    }
}
