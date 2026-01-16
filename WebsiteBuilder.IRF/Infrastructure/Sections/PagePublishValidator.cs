using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Sections;

public sealed class PagePublishValidator
{
    private readonly DataContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISectionValidationService _sectionValidation;

    public PagePublishValidator(
        DataContext db,
        ITenantContext tenant,
        ISectionValidationService sectionValidation)
    {
        _db = db;
        _tenant = tenant;
        _sectionValidation = sectionValidation;
    }

    public async Task<PagePublishValidationResult> ValidateDraftSectionsAsync(
        int pageId,
        CancellationToken ct = default)
    {
        var result = new PagePublishValidationResult();

        if (!_tenant.IsResolved)
        {
            result.Errors.Add(new PageSectionPublishError
            {
                SectionId = 0,
                SectionTypeId = 0,
                TypeKey = "Tenant",
                Messages = new List<string> { "Tenant not resolved." }
            });
            return result;
        }

        // Pull the current sections for this page
        // IMPORTANT: ensure PageSection has PageId (or adjust to your actual FK).
        var sections = await _db.PageSections
            .AsNoTracking()
            .Where(s =>
                s.TenantId == _tenant.TenantId &&
                s.PageId == pageId &&
                !s.IsDeleted && s.IsActive)
            .OrderBy(s => s.SortOrder)
            .Select(s => new
            {
                s.Id,
                s.SectionTypeId,
                s.SettingsJson,
                TypeKey = s.SectionType!.Key // requires navigation OR join; see note below
            })
            .ToListAsync(ct);

        // Rule: cannot publish a page with no sections
        if (sections.Count == 0)
        {
            result.Errors.Add(new PageSectionPublishError
            {
                SectionId = 0,
                SectionTypeId = 0,
                TypeKey = "Page",
                Messages = new List<string> { "This page has no sections. Add at least one section before publishing." }
            });
            return result;
        }

        foreach (var s in sections)
        {
            var typeKey = SectionTypeKeyMap.GetKey(s.SectionTypeId);

            if (typeKey == "Unknown")
            {
                result.Errors.Add(new PageSectionPublishError
                {
                    SectionId = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    TypeKey = typeKey,
                    Messages = new List<string>
                    {
                        $"Unknown SectionTypeId '{s.SectionTypeId}'. Cannot validate this section."
                    }
                });
                continue;
            }

            // Your existing validators validate SettingsJson (and parse it internally).
            // ValidateAsync(typeKey, json) is also supported in your service interface.
            var sectionResult = await _sectionValidation.ValidateAsync(typeKey, s.SettingsJson);

            if (!sectionResult.IsValid)
            {
                result.Errors.Add(new PageSectionPublishError
                {
                    SectionId = s.Id,
                    SectionTypeId = s.SectionTypeId,
                    TypeKey = typeKey,
                    Messages = sectionResult.Errors.ToList()
                });
            }
        }

        return result;
    }
}

public sealed class PagePublishValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<PageSectionPublishError> Errors { get; } = new();
}

public sealed class PageSectionPublishError
{
    public int SectionId { get; set; }
    public int SectionTypeId { get; set; }
    public string TypeKey { get; set; } = "";
    public List<string> Messages { get; set; } = new();
}

public static class SectionTypeKeyMap
{
    public const int Hero = 1;
    public const int Text = 2;
    public const int Gallery = 3;

    // IMPORTANT: must match your validators' TypeKey exactly: "Hero", "Text", "Gallery"
    public static string GetKey(int sectionTypeId) => sectionTypeId switch
    {
        Hero => "Hero",
        Text => "Text",
        Gallery => "Gallery",
        _ => "Unknown"
    };
}
