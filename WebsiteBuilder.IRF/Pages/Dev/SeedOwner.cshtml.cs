using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Dev
{
    // Dev-only page. Keep it locked down.
    [AllowAnonymous] // You can change to [Authorize] later
    public class SeedOwnerModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public SeedOwnerModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? Message { get; private set; }

        public class InputModel
        {
            public string Email { get; set; } = "owner@demo.com";
            public string Password { get; set; } = "P@ssw0rd123!";
            public string FirstName { get; set; } = "Chris";
            public string LastName { get; set; } = "Owner";
        }

        public async Task OnGetAsync()
        {
            // No-op
            await Task.CompletedTask;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            var normalizedEmail = Input.Email.Trim();

            var existing = await _userManager.FindByEmailAsync(normalizedEmail);
            if (existing != null)
            {
                Message = $"User already exists. UserId: {existing.Id}";
                return Page();
            }

            var user = new ApplicationUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                EmailConfirmed = true,
                FirstName = Input.FirstName.Trim(),
                LastName = Input.LastName.Trim(),
                RegistrationDate = DateTime.UtcNow,

                // IMPORTANT:
                // Leave TenantId EMPTY for now; set it AFTER you create the tenant.
                TenantId = Guid.Empty
            };

            var result = await _userManager.CreateAsync(user, Input.Password);

            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(string.Empty, $"{err.Code}: {err.Description}");

                return Page();
            }

            Message = $"Owner created successfully. UserId: {user.Id}";
            return Page();
        }
    }
}
