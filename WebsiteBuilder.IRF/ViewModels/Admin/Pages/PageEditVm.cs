using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageEditVm
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        // URL path segment per tenant, e.g. "about", "listings"
        // If you want to allow "" for home, relax validation here.
        [Required, MaxLength(200)]
        public string Slug { get; set; } = string.Empty;

        [Required]
        public int PageStatusId { get; set; }

        [MaxLength(100)]
        public string? LayoutKey { get; set; }

        [MaxLength(200)]
        public string? MetaTitle { get; set; }

        [MaxLength(500)]
        public string? MetaDescription { get; set; }

        public int? OgImageAssetId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
