using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Pages;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;
using Page = WebsiteBuilder.Models.Page;

namespace WebsiteBuilder.IRF.Pages.Admin.Pages;

public sealed class CreateModel : PageModel
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;

    public CreateModel(DataContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public sealed class InputModel
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = "";

        [MaxLength(200)]
        public string? Slug { get; set; }

        [MaxLength(100)]
        public string? LayoutKey { get; set; }

        [MaxLength(200)]
        public string? MetaTitle { get; set; }

        [MaxLength(500)]
        public string? MetaDescription { get; set; }

        public bool ShowInNavigation { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
        // no-op
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!_tenant.IsResolved)
            return NotFound("Tenant not resolved.");

        if (!ModelState.IsValid)
            return Page();

        var slug = SlugUtil.Normalize(Input.Slug);

        // ensure unique per tenant
        var exists = await _db.Pages.AsNoTracking()
            .AnyAsync(p => p.TenantId == _tenant.TenantId && !p.IsDeleted && p.Slug == slug, ct);

        if (exists)
        {
            ModelState.AddModelError(nameof(Input.Slug), "Slug already exists for this tenant.");
            return Page();
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdStr, out var ownerId);

        var page = new Page
        {
            TenantId = _tenant.TenantId,
            Title = Input.Title.Trim(),
            Slug = slug,
            LayoutKey = Input.LayoutKey?.Trim(),
            MetaTitle = Input.MetaTitle?.Trim(),
            MetaDescription = Input.MetaDescription?.Trim(),
            ShowInNavigation = Input.ShowInNavigation,
            PageStatusId = PageStatusIds.Draft,
            OwnerUserId = ownerId
        };

        _db.Pages.Add(page);
        await _db.SaveChangesAsync(ct);

        // create initial draft revision (not a published snapshot)
        var draft = new PageRevision
        {
            TenantId = _tenant.TenantId,
            PageId = page.Id,
            VersionNumber = 1,
            IsPublishedSnapshot = false,
            Title = page.Title,
            Slug = page.Slug,
            LayoutKey = page.LayoutKey ?? "",
            MetaTitle = page.MetaTitle ?? "",
            MetaDescription = page.MetaDescription ?? "",
            OgImageAssetId = page.OgImageAssetId
        };

        _db.PageRevisions.Add(draft);
        await _db.SaveChangesAsync(ct);

        page.DraftRevisionId = draft.Id;
        await _db.SaveChangesAsync(ct);

        return RedirectToPage("/Admin/Pages/Edit", new { id = page.Id });
    }
}
