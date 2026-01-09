using System;
using System.ComponentModel.DataAnnotations;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.Models
{
    public class TenantUser : TenantBaseModel
    {
        [Key]
        public int Id { get; set; }

        public Guid UserId { get; set; }

        [MaxLength(50)]
        public string Role { get; set; } = "Owner"; // Owner/Admin/Editor/Billing
    }
}
