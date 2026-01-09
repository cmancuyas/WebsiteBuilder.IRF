using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class Plan : BaseModel   
    {
        [Required, MaxLength(100)]
        public string Code { get; set; } = string.Empty;
        public double Price { get; set; }
    }
}
