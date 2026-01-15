using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Media;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Pages.Admin.Media
{
    public class UploadModel : PageModel
    {
        private readonly DataContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ITenantContext _tenant;
        private readonly ITenantMediaQuotaService _quota;

        public UploadModel(DataContext db, IWebHostEnvironment env, ITenantContext tenant, ITenantMediaQuotaService quota)
        {
            _db = db;
            _env = env;
            _tenant = tenant;
            _quota = quota;
        }

        public const long MaxBytes = 5 * 1024 * 1024; // 5 MB
        public string MaxBytesDisplay => "5 MB";

        [BindProperty]
        public IFormFile? Upload { get; set; }

        public string? Error { get; private set; }
        public string? SuccessUrl { get; private set; }

        public IActionResult OnGet()
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!_tenant.IsResolved)
                return NotFound("Tenant not resolved.");

            var result = await HandleUploadAsync(GetPostedFile(null), HttpContext.RequestAborted);

            if (!result.success)
            {
                Error = result.error;
                return Page();
            }

            SuccessUrl = result.publicUrl;
            return Page();
        }

        // POST /Admin/Media/Upload?handler=Json
        public async Task<IActionResult> OnPostJsonAsync(IFormFile? file)
        {
            if (!_tenant.IsResolved)
                return new JsonResult(new { success = false, error = "Tenant not resolved." }) { StatusCode = 400 };

            var posted = GetPostedFile(file);
            var result = await HandleUploadAsync(posted, HttpContext.RequestAborted);

            if (!result.success)
                return new JsonResult(new { success = false, error = result.error }) { StatusCode = 400 };

            return new JsonResult(new
            {
                success = true,
                asset = new
                {
                    id = result.asset!.Id,
                    tenantId = result.asset.TenantId,
                    fileName = result.asset.FileName,
                    contentType = result.asset.ContentType,
                    storageKey = result.asset.StorageKey,
                    thumbStorageKey = result.asset.ThumbStorageKey,
                    width = result.asset.Width,
                    height = result.asset.Height,
                    sizeBytes = result.asset.SizeBytes,
                    checksum = result.asset.CheckSum
                },
                url = result.publicUrl,
                thumbUrl = result.thumbUrl
            });
        }

        private IFormFile? GetPostedFile(IFormFile? file)
        {
            if (file != null) return file;
            if (Upload != null) return Upload;
            return Request?.Form?.Files?.FirstOrDefault();
        }

        private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
            "image/gif"
        };

        private static readonly Dictionary<string, string> ExtByContentType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif"
        };

        private async Task<(bool success, string? error, MediaAsset? asset, string? publicUrl, string? thumbUrl)>
            HandleUploadAsync(IFormFile? file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return (false, "Please choose an image to upload.", null, null, null);

            if (file.Length > MaxBytes)
                return (false, $"File too large. Max allowed is {MaxBytesDisplay}.", null, null, null);

            var contentType = (file.ContentType ?? string.Empty).Trim();
            if (!AllowedContentTypes.Contains(contentType))
                return (false, "Invalid file type. Only JPG, PNG, WEBP, and GIF are allowed.", null, null, null);

            var ext = ExtByContentType.TryGetValue(contentType, out var e)
                ? e
                : Path.GetExtension(file.FileName);

            if (string.IsNullOrWhiteSpace(ext))
                return (false, "Cannot determine file extension.", null, null, null);

            var webRoot = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
                return (false, "WebRootPath is not configured. Ensure app.UseStaticFiles() and wwwroot exist.", null, null, null);

            // Quota check (must be before disk write)
            var (allowed, usedBytes, quotaBytes) = await _quota.CanUploadAsync(_tenant.TenantId, file.Length, ct);
            if (!allowed)
            {
                return (false,
                    $"Storage quota exceeded. Used {usedBytes:N0} bytes out of {quotaBytes:N0} bytes.",
                    null, null, null);
            }

            // Originals: wwwroot/uploads/yyyy/MM/{guid}{ext}
            var now = DateTime.UtcNow;
            var relativeDir = Path.Combine("uploads", now.Year.ToString("0000"), now.Month.ToString("00"));
            var physicalDir = Path.Combine(webRoot, relativeDir);
            Directory.CreateDirectory(physicalDir);

            var safeName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var physicalPath = Path.Combine(physicalDir, safeName);

            // Public URL of original
            var publicUrl = "/" + Path.Combine(relativeDir, safeName).Replace('\\', '/');

            // Prepare thumb paths (only created if we successfully generate)
            var thumbRelativeDir = Path.Combine("uploads", "thumbs", now.Year.ToString("0000"), now.Month.ToString("00"));
            var thumbPhysicalDir = Path.Combine(webRoot, thumbRelativeDir);
            Directory.CreateDirectory(thumbPhysicalDir);

            var thumbFileName = $"{Path.GetFileNameWithoutExtension(safeName)}.webp";
            var thumbPhysicalPath = Path.Combine(thumbPhysicalDir, thumbFileName);
            string? thumbUrl = "/" + Path.Combine(thumbRelativeDir, thumbFileName).Replace('\\', '/');

            // 1) Write original file
            await using (var fs = System.IO.File.Create(physicalPath))
            {
                await file.CopyToAsync(fs, ct);
            }

            // 2) Validate it is a real image + capture dimensions (early)
            int? width = null;
            int? height = null;

            Image? originalImage = null;
            try
            {
                originalImage = await Image.LoadAsync(physicalPath, ct);
                width = originalImage.Width;
                height = originalImage.Height;
            }
            catch
            {
                TryDeletePhysical(physicalPath);
                // thumb not created yet, so nothing to delete
                return (false, "Invalid image file.", null, null, null);
            }

            // 3) SHA-256 checksum
            string checksumHex;
            await using (var fs = System.IO.File.OpenRead(physicalPath))
            {
                using var sha = SHA256.Create();
                var hash = await sha.ComputeHashAsync(fs, ct);
                checksumHex = Convert.ToHexString(hash).ToLowerInvariant();
            }

            // 4) Dedupe check (before thumbnail generation)
            var existing = await _db.Set<MediaAsset>()
                .AsNoTracking()
                .Where(m => m.TenantId == _tenant.TenantId && !m.IsDeleted && m.CheckSum == checksumHex)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                // Remove the newly uploaded duplicate file (no thumb created yet)
                TryDeletePhysical(physicalPath);
                originalImage.Dispose();

                return (true, null, existing,
                    existing.StorageKey,
                    string.IsNullOrWhiteSpace(existing.ThumbStorageKey) ? null : existing.ThumbStorageKey);
            }

            // 5) Generate thumbnail (max 320px on longest side)
            try
            {
                using var thumb = originalImage.Clone(ctx => ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(320, 320)
                }));

                await thumb.SaveAsync(thumbPhysicalPath, new WebpEncoder { Quality = 75 }, ct);
            }
            catch
            {
                // Non-breaking: if thumb fails, keep ThumbStorageKey empty
                thumbUrl = null;
            }
            finally
            {
                originalImage.Dispose();
            }

            // 6) Persist MediaAsset
            var asset = new MediaAsset
            {
                TenantId = _tenant.TenantId,
                FileName = Path.GetFileName(file.FileName),
                ContentType = contentType,
                SizeBytes = file.Length.ToString(),
                StorageKey = publicUrl,
                ThumbStorageKey = thumbUrl ?? string.Empty,
                Width = width?.ToString() ?? string.Empty,
                Height = height?.ToString() ?? string.Empty,
                AltText = string.Empty,
                CheckSum = checksumHex,
                CreatedAt = DateTime.UtcNow
            };

            _db.Set<MediaAsset>().Add(asset);
            await _db.SaveChangesAsync(ct);

            return (true, null, asset, publicUrl, thumbUrl);
        }

        private static void TryDeletePhysical(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
