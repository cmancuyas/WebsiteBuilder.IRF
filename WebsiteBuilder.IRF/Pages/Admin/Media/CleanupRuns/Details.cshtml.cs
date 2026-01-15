using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Media.CleanupRuns;

public sealed class DetailsModel : PageModel
{
    private readonly DataContext _db;

    public DetailsModel(DataContext db)
    {
        _db = db;
    }

    public MediaCleanupRunLog? Run { get; private set; }
    public string StatusBadge { get; private set; } = "bg-secondary";

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        Run = await _db.Set<MediaCleanupRunLog>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (Run is null)
            return Page();

        StatusBadge = (Run.Status ?? "").Trim() switch
        {
            "Succeeded" => "bg-success",
            "Partial" => "bg-warning text-dark",
            "Failed" => "bg-danger",
            "Running" => "bg-info text-dark",
            _ => "bg-secondary"
        };

        return Page();
    }
}
