using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Media.Alerts;

public sealed class IndexModel : PageModel
{
    private readonly DataContext _db;

    public IndexModel(DataContext db)
    {
        _db = db;
    }

    public List<MediaAlert> Alerts { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Alerts = await _db.MediaAlerts
            .AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
    }

    public string GetSeverityBadgeClass(string? severity)
    {
        severity = (severity ?? "").Trim();

        return severity switch
        {
            "Error" => "bg-danger",
            "Warning" => "bg-warning text-dark",
            "Info" => "bg-info text-dark",
            _ => "bg-secondary"
        };
    }
}
