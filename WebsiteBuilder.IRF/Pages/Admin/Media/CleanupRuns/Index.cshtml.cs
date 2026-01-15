using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Media;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Media.CleanupRuns;

public sealed class IndexModel : PageModel
{
    private readonly DataContext _db;
    private readonly IMediaCleanupRunner _runner;

    public IndexModel(DataContext db, IMediaCleanupRunner runner)
    {
        _db = db;
        _runner = runner;
    }

    public List<MediaCleanupRunLog> Runs { get; private set; } = new();
    public MediaCleanupRunLog? LastRun { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        // If you later fix TenantId typing, filter here by tenant.
        Runs = await _db.Set<MediaCleanupRunLog>()
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(25)
            .ToListAsync(ct);

        LastRun = Runs.FirstOrDefault();
    }

    public async Task<IActionResult> OnPostRunNowAsync(CancellationToken ct)
    {
        // Manual trigger. Runner should log the run.
        await _runner.RunOnceAsync(ct);
        return RedirectToPage();
    }

    public string GetStatusBadgeClass(string? status)
    {
        status = (status ?? "").Trim();

        return status switch
        {
            "Succeeded" => "bg-success",
            "Partial" => "bg-warning text-dark",
            "Failed" => "bg-danger",
            "Running" => "bg-info text-dark",
            _ => "bg-secondary"
        };
    }
}
