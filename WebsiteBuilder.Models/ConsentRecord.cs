using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class ConsentRecord : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public int LeadId { get; set; }
        [MaxLength(100)]
        public string Channel { get; set; } = string.Empty;
        [MaxLength(100)]
        public string ConsentType { get; set; } = string.Empty;
        public DateTime? GrantedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        [MaxLength(200)]
        public string Source { get; set; } = string.Empty;
        [MaxLength(45)]
        public string IpAddress { get; set; } = string.Empty;
        [MaxLength(500)]
        public string? UserAgent { get; set; } = string.Empty;
    }
}
