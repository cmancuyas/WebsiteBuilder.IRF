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

        // ✅ Canonical host used for matching + uniqueness (lowercase, no trailing dot, punycode)
        [Required, MaxLength(510)]
        public string NormalizedHost { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        public int VerificationStatusId { get; set; }
        public int VerificationMethodId { get; set; }

        public int SslModeId { get; set; }

        public string? CertificateThumbprint { get; set; }

        public DateTime? ActivatedAt { get; set; }

        // ✅ Onboarding verification fields
        [Required, MaxLength(200)]
        public string VerificationToken { get; set; } = string.Empty;

        public DateTime? VerificationTokenCreatedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? LastVerificationCheckAt { get; set; }

        [MaxLength(2000)]
        public string? LastVerificationError { get; set; }

        public DateTime? DeactivatedAt { get; set; }

        // ✅ Navigation
        public Tenant? Tenant { get; set; }
    }
}