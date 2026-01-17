using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Sections;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Constants;

namespace WebsiteBuilder.IRF.Infrastructure.Pages
{
    public sealed class PagePublishingService : IPagePublishingService
    {
        private readonly DataContext _db;
        private readonly ITenantContext _tenant;
        private readonly ISectionValidationService _sectionValidation;

        public PagePublishingService(DataContext db, ITenantContext tenant, ISectionValidationService sectionValidation)
        {
            _db = db;
            _tenant = tenant;
            _sectionValidation = sectionValidation;
        }

        public async Task<PublishResult> PublishAsync(
            int pageId,
            Guid actorUserId,
            CancellationToken ct = default)
        {
            // IMPORTANT: SQL retry strategy + transactions require ExecuteAsync wrapper
            var strategy = _db.Database.CreateExecutionStrategy();

            try
            {
                return await strategy.ExecuteAsync(async () =>
                {
                    // Load page + draft pointer (tracked)
                    var page = await _db.Pages
                        .FirstOrDefaultAsync(p =>
                            p.Id == pageId &&
                            p.TenantId == _tenant.TenantId &&
                            !p.IsDeleted, ct);

                    if (page == null)
                        return PublishResult.Fail("Page not found.");

                    if (page.DraftRevisionId == null)
                        return PublishResult.Fail("Cannot publish: no draft revision found.");

                    // Load draft revision (AsNoTracking OK)
                    var draft = await _db.PageRevisions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r =>
                            r.Id == page.DraftRevisionId &&
                            r.TenantId == _tenant.TenantId &&
                            r.PageId == pageId &&
                            !r.IsDeleted, ct);

                    if (draft == null)
                        return PublishResult.Fail("Cannot publish: draft revision not found.");

                    await using var tx = await _db.Database.BeginTransactionAsync(ct);

                    // Load draft sections (TRACKED: we may persist gallery migrations)
                    var draftSections = await _db.PageRevisionSections
                        .Where(s =>
                            s.TenantId == _tenant.TenantId &&
                            s.PageRevisionId == draft.Id &&
                            !s.IsDeleted &&
                            s.IsActive)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.Id)
                        .ToListAsync(ct);

                    if (draftSections.Count == 0)
                        return PublishResult.Fail("Cannot publish: this page has no sections.");

                    // Resolve SectionTypeId -> Key once
                    var sectionTypeIds = draftSections.Select(s => s.SectionTypeId).Distinct().ToList();

                    var sectionTypeKeys = await _db.SectionTypes
                        .AsNoTracking()
                        .Where(st => sectionTypeIds.Contains(st.Id) && st.IsActive && !st.IsDeleted)
                        .ToDictionaryAsync(st => st.Id, st => st.Key, ct);

                    var now = DateTime.UtcNow;

                    // 1) Auto-migrate legacy gallery JSON (items -> images) on draft sections
                    var anyMigrated = false;

                    foreach (var s in draftSections)
                    {
                        if (!sectionTypeKeys.TryGetValue(s.SectionTypeId, out var key) || string.IsNullOrWhiteSpace(key))
                            continue;

                        var typeKey = key.Trim().ToLowerInvariant();
                        if (typeKey != "gallery")
                            continue;

                        var json = string.IsNullOrWhiteSpace(s.SettingsJson) ? "{}" : s.SettingsJson.Trim();

                        if (GalleryJsonMigrator.TryMigrateLegacyItemsToImages(json, out var migrated, out _))
                        {
                            s.SettingsJson = migrated;
                            s.UpdatedAt = now;
                            s.UpdatedBy = actorUserId;
                            anyMigrated = true;
                        }
                    }

                    if (anyMigrated)
                        await _db.SaveChangesAsync(ct);

                    // 1.5) Resolve Gallery assetId -> url (validator requires url)
                    var galleryAssetIds = new HashSet<int>();

                    foreach (var s in draftSections)
                    {
                        if (!sectionTypeKeys.TryGetValue(s.SectionTypeId, out var key) || string.IsNullOrWhiteSpace(key))
                            continue;

                        if (!string.Equals(key.Trim(), "gallery", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.IsNullOrWhiteSpace(s.SettingsJson))
                            continue;

                        using var doc = JsonDocument.Parse(s.SettingsJson);
                        if (!doc.RootElement.TryGetProperty("images", out var arr) || arr.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var img in arr.EnumerateArray())
                        {
                            if (img.ValueKind != JsonValueKind.Object)
                                continue;

                            if (img.TryGetProperty("assetId", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                            {
                                var id = idProp.GetInt32();
                                if (id > 0) galleryAssetIds.Add(id);
                            }
                        }
                    }

                    if (galleryAssetIds.Count > 0)
                    {
                        // Pull assets (tenant scoped) and build url map
                        var assets = await _db.MediaAssets
                            .AsNoTracking()
                            .Where(a =>
                                a.TenantId == _tenant.TenantId &&
                                !a.IsDeleted &&
                                a.IsActive &&
                                galleryAssetIds.Contains(a.Id))
                            .ToListAsync(ct);

                        var assetUrlMap = assets.ToDictionary(a => a.Id, a => BuildMediaUrl(a));

                        // Rewrite draft section JSON: assetId -> url (remove assetId)
                        foreach (var s in draftSections)
                        {
                            if (string.IsNullOrWhiteSpace(s.SettingsJson))
                                continue;

                            var rootNode = JsonNode.Parse(s.SettingsJson) as JsonObject;
                            if (rootNode == null || rootNode["images"] is not JsonArray images)
                                continue;

                            var changed = false;

                            foreach (var imgNode in images)
                            {
                                if (imgNode is not JsonObject imgObj)
                                    continue;

                                if (!imgObj.TryGetPropertyValue("assetId", out var idNode) || idNode is null)
                                    continue;

                                int id;
                                try { id = idNode.GetValue<int>(); }
                                catch { continue; }

                                if (!assetUrlMap.TryGetValue(id, out var url))
                                    continue; // leave as-is; validation will fail (correctly) if url missing

                                imgObj["url"] = url;
                                imgObj.Remove("assetId");
                                changed = true;
                            }

                            if (changed)
                            {
                                s.SettingsJson = rootNode.ToJsonString();
                                s.UpdatedAt = now;
                                s.UpdatedBy = actorUserId;
                            }
                        }

                        await _db.SaveChangesAsync(ct);
                    }

                    // 2) Validate all sections (after migration + url resolution)
                    var errors = new List<string>();

                    foreach (var s in draftSections)
                    {
                        if (!sectionTypeKeys.TryGetValue(s.SectionTypeId, out var key) || string.IsNullOrWhiteSpace(key))
                        {
                            errors.Add($"SectionTypeId '{s.SectionTypeId}': missing SectionTypes.Key.");
                            continue;
                        }

                        var typeKey = key.Trim().ToLowerInvariant();
                        var json = string.IsNullOrWhiteSpace(s.SettingsJson) ? "{}" : s.SettingsJson.Trim();

                        var validation = await _sectionValidation.ValidateAsync(typeKey, json);
                        if (!validation.IsValid)
                            errors.AddRange(validation.Errors.Select(e => $"Section '{typeKey}': {e}"));
                    }

                    if (errors.Count > 0)
                        return new PublishResult { Success = false, Errors = errors };

                    var nextVersion =
                        (await _db.PageRevisions
                            .Where(r => r.TenantId == _tenant.TenantId &&
                                        r.PageId == pageId &&
                                        !r.IsDeleted)
                            .MaxAsync(r => (int?)r.VersionNumber, ct)
                        ?? 0) + 1;

                    // 3) Create published snapshot revision from draft content
                    var publishedRevision = new PageRevision
                    {
                        TenantId = _tenant.TenantId,
                        PageId = pageId,
                        VersionNumber = nextVersion,
                        IsPublishedSnapshot = true,

                        Title = draft.Title,
                        Slug = draft.Slug,
                        LayoutKey = draft.LayoutKey ?? string.Empty,
                        MetaTitle = draft.MetaTitle ?? string.Empty,
                        MetaDescription = draft.MetaDescription ?? string.Empty,
                        OgImageAssetId = draft.OgImageAssetId,
                        PublishedAt = now,

                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        CreatedBy = actorUserId
                    };

                    foreach (var s in draftSections)
                    {
                        publishedRevision.Sections.Add(new PageRevisionSection
                        {
                            TenantId = _tenant.TenantId,
                            SourcePageSectionId = s.SourcePageSectionId,

                            SectionTypeId = s.SectionTypeId,
                            SortOrder = s.SortOrder,
                            SettingsJson = string.IsNullOrWhiteSpace(s.SettingsJson) ? "{}" : s.SettingsJson.Trim(),

                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = now,
                            CreatedBy = actorUserId
                        });
                    }

                    _db.PageRevisions.Add(publishedRevision);
                    await _db.SaveChangesAsync(ct);

                    // 4) Update canonical publish pointer on Page
                    page.PublishedRevisionId = publishedRevision.Id;
                    page.PublishedAt = now;
                    page.PageStatusId = PageStatusIds.Published;
                    page.UpdatedAt = now;
                    page.UpdatedBy = actorUserId;

                    await _db.SaveChangesAsync(ct);

                    // 5) Create a fresh draft revision cloned from the published snapshot
                    // FIX: avoid duplicate key by using a NEW version number
                    var newDraft = new PageRevision
                    {
                        TenantId = _tenant.TenantId,
                        PageId = pageId,
                        VersionNumber = nextVersion + 1,
                        IsPublishedSnapshot = false,

                        Title = publishedRevision.Title,
                        Slug = publishedRevision.Slug,
                        LayoutKey = publishedRevision.LayoutKey ?? string.Empty,
                        MetaTitle = publishedRevision.MetaTitle ?? string.Empty,
                        MetaDescription = publishedRevision.MetaDescription ?? string.Empty,
                        OgImageAssetId = publishedRevision.OgImageAssetId,

                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        CreatedBy = actorUserId
                    };

                    foreach (var s in publishedRevision.Sections.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                    {
                        newDraft.Sections.Add(new PageRevisionSection
                        {
                            TenantId = _tenant.TenantId,
                            SourcePageSectionId = s.SourcePageSectionId,

                            SectionTypeId = s.SectionTypeId,
                            SortOrder = s.SortOrder,
                            SettingsJson = s.SettingsJson,

                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = now,
                            CreatedBy = actorUserId
                        });
                    }

                    _db.PageRevisions.Add(newDraft);
                    await _db.SaveChangesAsync(ct);

                    page.DraftRevisionId = newDraft.Id;
                    page.UpdatedAt = now;
                    page.UpdatedBy = actorUserId;

                    await _db.SaveChangesAsync(ct);

                    await tx.CommitAsync(ct);

                    return PublishResult.Ok(publishedRevision.Id);
                });
            }
            catch (Exception ex)
            {
                // Return a friendly error to the UI (instead of throwing)
                return PublishResult.Fail("Publish failed: " + ex.Message);
            }
        }

        private static string BuildMediaUrl(MediaAsset asset)
        {
            // Update this route if your actual serving endpoint is different.
            return $"/media/{asset.Id}";
        }
    }
}
