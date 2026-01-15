using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public sealed class MediaAlert : BaseModel
    {
        public long Id { get; set; }

        public Guid TenantId { get; set; }

        [MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Severity { get; set; } = "Warning";

        // Link to cleanup run (optional)
        public long? MediaCleanupRunLogId { get; set; }
    }
}

