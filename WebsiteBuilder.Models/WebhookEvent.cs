using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class WebhookEvent : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public int ProviderId { get; set; }
        public int EventId { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime ProcessedAt { get; set; }
        public int ProcessingStatusId { get; set; }
        [MaxLength(4000)]
        public string RawPayload { get; set; } = string.Empty;


        public Provider? Provider { get; set; }
        public ProcessingStatus? ProcessingStatus { get; set; }
    }
}
