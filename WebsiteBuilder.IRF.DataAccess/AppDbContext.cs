using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.DataAccess
{
    public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {

        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ApplicationUser>().ToTable("AspNetUsers");
            modelBuilder.Entity<ApplicationRole>().ToTable("AspNetRoles");

        }
    }
}
