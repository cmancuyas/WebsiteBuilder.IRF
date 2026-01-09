using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class DomainMapping : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        // e.g. "agentdomain.com" or "agent.yourplatform.com"
        [Required, MaxLength(255)]
        public string Host { get; set; } = string.Empty;

        public bool IsPrimary { get; set; } = false;

        public int VerificationStatusId { get; set; }
        public int VerificationMethodId { get; set; }
        public int SslModeId { get; set; }

        // For customer-provided certificates, if you support it.
        // If platform-managed SSL, this can be null.
        [MaxLength(128)]
        public string? CertificateThumbprint { get; set; }

        public DateTime? ActivatedAt { get; set; }
    }
}
