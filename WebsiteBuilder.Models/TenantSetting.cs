using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class TenantSetting : BaseModel
    {
        [Key]
        public int Id { get; set; }

        public Guid TenantId { get; set; }

        [Required, MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Value { get; set; }
    }
}
