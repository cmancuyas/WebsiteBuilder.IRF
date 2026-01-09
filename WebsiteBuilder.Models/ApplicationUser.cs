using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace WebsiteBuilder.Models
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public Guid TenantId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string? Suffix { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime RegistrationDate { get; set; }

    }
}
