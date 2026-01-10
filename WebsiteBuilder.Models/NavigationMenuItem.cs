using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class NavigationMenuItem : BaseModel
    {
        [Key]
        public int Id { get; set; }

        public Guid TenantId { get; set; }

        public int MenuId { get; set; }
        public int? ParentId { get; set; }

        [MaxLength(200)]
        public string Label { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Url { get; set; } = string.Empty;

        public bool OpenInNewTab { get; set; }
    }
}
