using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class Subscription : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int PlanId { get; set; }
        public int PlanStatusId { get; set; }
        public DateTime CurrentPeriodStart { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
        public int ExternalCustomerId { get; set; }
        public DateTime CancelAtPeriodEnd { get; set; }
        public DateTime TrialEndsAt { get; set; }

        public PlanStatus? PlanStatus { get; set; }
    }
}
