using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PageSection : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        // FK to Page
        public int PageId { get; set; }

        // e.g. Hero/Text/Gallery/ListingsGrid/ContactForm
        public int SectionTypeId { get; set; }

        // Order within the page
        public int SortOrder { get; set; }

        // JSON payload for section settings (no short max length)
        [Required]
        public string SettingsJson { get; set; } = "{}";

        // Navigation (recommended)
        public Page? Page { get; set; }
        public SectionType? SectionType { get; set; }
    }
}
