using Microsoft.AspNetCore.Identity;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Auth
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
        {
            using var scope = services.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            foreach (var roleName in new[] { AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Agent })
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var role = new ApplicationRole(roleName)
                    {
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = Guid.Empty
                    };
                    var res = await roleManager.CreateAsync(role);
                    if (!res.Succeeded)
                        throw new InvalidOperationException("Role create failed: " + string.Join("; ", res.Errors.Select(e => e.Description)));
                }
            }

            var superAdmins = await userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
            if (superAdmins.Count > 1)
                throw new InvalidOperationException("More than one SuperAdmin exists.");

            if (superAdmins.Count == 1)
                return;

            var email = config["Bootstrap:SuperAdmin:Email"] ?? "superadmin@irfconnect.com";
            var password = config["Bootstrap:SuperAdmin:Password"] ?? "aMyhurLX9Ad94D!";

            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    TenantId = Guid.Empty,
                    FirstName = "Super",
                    LastName = "Admin",
                    RegistrationDate = DateTime.UtcNow
                };

                var create = await userManager.CreateAsync(user, password);
                if (!create.Succeeded)
                    throw new InvalidOperationException("SuperAdmin create failed: " + string.Join("; ", create.Errors.Select(e => e.Description)));
            }

            var addRole = await userManager.AddToRoleAsync(user, AppRoles.SuperAdmin);
            if (!addRole.Succeeded)
                throw new InvalidOperationException("Assign SuperAdmin failed: " + string.Join("; ", addRole.Errors.Select(e => e.Description)));
        }
    }
}
