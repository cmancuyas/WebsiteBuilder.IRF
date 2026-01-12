using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class ListingStatusHistory : BaseModel
    {
        public int Id { get; set; }
        public int ListingId { get; set; }
        public int OldStatusId { get; set; }
        public int NewStatusId { get; set; }
        public string? Reason { get; set; }
    }

}
