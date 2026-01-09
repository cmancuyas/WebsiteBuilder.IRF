using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class CommunicationMessage : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int LeadId { get; set; }
        [MaxLength(100)]
        public string Channel { get; set; } = string.Empty;
        [MaxLength(100)]
        public string TemplateKey { get; set; } = string.Empty;
        [MaxLength(100)]
        public string To { get; set; } = string.Empty;
        [MaxLength(100)]
        public string Status { get; set; } = string.Empty;
        public int ProviderMessageId { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public string Error { get; set; } = string.Empty;
    }
}
