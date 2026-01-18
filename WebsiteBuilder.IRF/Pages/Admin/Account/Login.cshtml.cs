using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebsiteBuilder.IRF.Infrastructure.Auth;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public LoginModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ErrorMessage { get; set; }

        public sealed class InputModel
        {
            [Required, EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            public string Password { get; set; } = string.Empty;

            public bool RememberMe { get; set; } = true;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                ErrorMessage = "Invalid login.";
                return Page();
            }

            // Only allow platform/tenant admins + agents to log into /Admin
            var allowed =
                await _userManager.IsInRoleAsync(user, AppRoles.SuperAdmin) ||
                await _userManager.IsInRoleAsync(user, AppRoles.Admin) ||
                await _userManager.IsInRoleAsync(user, AppRoles.Agent);

            if (!allowed)
            {
                ErrorMessage = "Access denied.";
                return Page();
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName!,
                Input.Password,
                isPersistent: Input.RememberMe,
                lockoutOnFailure: false);

            if (!result.Succeeded)
            {
                ErrorMessage = "Invalid login.";
                return Page();
            }

            // safe redirect
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            return LocalRedirect("/Admin");
        }
    }
}
