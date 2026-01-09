using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class ListingStatusHistory : BaseModel
    {
        [Key]
        public int Id { get; set; }
        public string OldStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
    }
}
