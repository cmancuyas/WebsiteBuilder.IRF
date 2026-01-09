using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class AuditLog : BaseModel
    {
        [Key]
        public int Id { get; set; }
    }
}
