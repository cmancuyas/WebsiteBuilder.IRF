using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class ListingAddress : BaseModel
    {
        public int Id { get; set; }
        [MaxLength(200)]
        public string Street1 { get; set; } = string.Empty;
        [MaxLength(200)]
        public string Street2 { get; set; } = string.Empty;
        [MaxLength(200)]
        public string City { get; set; } = string.Empty;
        [MaxLength(100)]
        public string State { get; set; } = string.Empty;
        [MaxLength(20)]
        public string ZipCode { get; set; } = string.Empty;
        [MaxLength(100)]
        public string Country { get; set; } = string.Empty;
    }
}
