using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class PaymentTransaction : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public int TenantId { get; set; }
        public double Amount { get; set; }
        public int CurrencyId { get; set; }
        public int ExternalPaymentId { get; set; }
        public int PaymentStatusId { get; set; }
        public string RawPayload { get; set; } = string.Empty;

        public Currency? Currency { get; set; }
        public PaymentStatus? PaymentStatus { get; set; }
    }
}
