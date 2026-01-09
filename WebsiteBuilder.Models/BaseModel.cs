using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class BaseModel
    {
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public Guid? DeletedBy { get; set; }
        public DateTime? DeletedAt { get; set; }

        // Concurrency token (prevents silent overwrites in the builder)
        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
