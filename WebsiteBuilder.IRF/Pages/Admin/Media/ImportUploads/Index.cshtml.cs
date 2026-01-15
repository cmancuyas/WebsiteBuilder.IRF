using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Media.ImportUploads;

public sealed class IndexModel : PageModel
{
    private readonly DataContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContext _tenant;

    public IndexModel(DataContext db, IWebHostEnvironment env, ITenantContext tenant)
    {
        _db = db;
        _env = env;
        _tenant = tenant;
    }

    public string? ResultMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostImportAsync(int max = 50, CancellationToken ct = default)
    {
        max = Math.Clamp(max, 1, 500);

        if (!_tenant.IsResolved)
        {
            ResultMessage = "Tenant is not resolved; cannot import.";
            return Page();
        }

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsDir))
        {
            ResultMessage = $"Uploads folder not found: {uploadsDir}";
            return Page();
        }

        // Read existing storage keys for tenant (avoid duplicates)
        var existingKeys = await _db.MediaAssets
            .AsNoTracking()
            .Where(m => m.TenantId == _tenant.TenantId)
            .Select(m => m.StorageKey)
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .EnumerateFiles(uploadsDir, "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .Take(max)
            .ToList();

        int imported = 0;
        int skipped = 0;

        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            var storageKey = "/uploads/" + fileName;

            if (existingSet.Contains(storageKey))
            {
                skipped++;
                continue;
            }

            var fi = new FileInfo(path);
            var contentType = GuessContentType(fileName);
            var checksum = await ComputeSha256HexAsync(path, ct);

            var asset = new MediaAsset
            {
                TenantId = _tenant.TenantId,
                FileName = fileName,
                ContentType = contentType,
                SizeBytes = fi.Length,
                StorageKey = storageKey,
                ThumbStorageKey = null,

                Width = null,
                Height = null,
                AltText = null,
                CheckSum = checksum,

                IsActive = true,
                IsDeleted = false,
                CreatedBy = Guid.Empty,
                CreatedAt = DateTime.UtcNow
            };

            _db.MediaAssets.Add(asset);
            imported++;
            existingSet.Add(storageKey);
        }

        await _db.SaveChangesAsync(ct);

        ResultMessage = $"Imported: {imported}, Skipped (already in DB): {skipped}, Scanned: {files.Count}.";
        return Page();
    }

    private static string GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct)
    {
        await using var stream = System.IO.File.OpenRead(path);
        using var sha = SHA256.Create();

        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

}
