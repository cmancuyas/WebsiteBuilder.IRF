using System;
using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageEditVm
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        // Required slug ("" for home is NOT allowed in your current model)
        [Required, MaxLength(200)]
        [RegularExpression(
            @"^[a-z0-9]+(?:-[a-z0-9]+)*$",
            ErrorMessage = "Slug must be lowercase letters/numbers with optional hyphens."
        )]
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

        public DateTime? PublishedAt { get; set; }

        public bool ShowInNavigation { get; set; } = true;

        public int NavigationOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public bool IsDeleted { get; set; } = false;
    }
}
