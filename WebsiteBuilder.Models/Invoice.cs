using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class Invoice : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int ExternalVoiceId { get; set; }
        public double AmountDue { get; set; }
        public double AmountPaid { get; set; }
        public int CurrencyId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        [MaxLength(100)]
        public string PdfUrl { get; set; } = string.Empty;
        public int BillingStatusId { get; set; }

        public Currency? Currency { get; set; }
        public BillingStatus? BillingStatus { get; set; }
    }
}
