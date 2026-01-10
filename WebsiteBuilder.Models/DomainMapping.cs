using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class DomainMapping : BaseModel
    {
        [Key]
        public int Id { get; set; }

        public Guid TenantId { get; set; }

        [Required, MaxLength(510)]
        public string Host { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        public int VerificationStatusId { get; set; }
        public int VerificationMethodId { get; set; }

        public int SslModeId { get; set; }

        public string? CertificateThumbprint { get; set; }
        public DateTime? ActivatedAt { get; set; }

        // ✅ Add this
        public Tenant? Tenant { get; set; }
    }
}
