using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.Models.Base
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

        // SQL Server rowversion / timestamp (optimistic concurrency)
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
