using Microsoft.AspNetCore.Identity;
using System.Xml.Linq;

namespace WebsiteBuilder.Models
{
    public class ApplicationRole : IdentityRole<Guid>
    {
        public ApplicationRole()
        {

        }
        public ApplicationRole(string roleName) : this()
        {
            Name = roleName;
        }
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? DeletedBy { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
