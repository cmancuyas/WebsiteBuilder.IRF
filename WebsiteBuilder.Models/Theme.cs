using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class Theme : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // Store CSS tokens/settings safely as JSON.
        // Do NOT cap to 100 chars; JSON will exceed that.
        [Required]
        public string TokensJson { get; set; } = "{}";

        [MaxLength(50)]
        public string Version { get; set; } = "1.0";
    }
}
