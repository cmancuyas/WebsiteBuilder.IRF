using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class Lead : BaseModel
    {
        [Key]
        public int Id { get; set; }
    }
}
