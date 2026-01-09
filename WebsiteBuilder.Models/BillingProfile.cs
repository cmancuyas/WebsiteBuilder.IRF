using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class BillingProfile : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public int TenantId { get; set; }
        [Required, MaxLength(200)]
        public string LegalName { get; set; } = string.Empty;
        [MaxLength(100)]
        public string PhoneNumber { get; set; } = string.Empty;
        public string BillingAddress { get; set; } = string.Empty;
        public bool IsTaxExempt { get; set; }
        public string TaxExemptCertificateRef { get; set; } = string.Empty;
    }
}
