using Microsoft.AspNetCore.OutputCaching;
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
        private readonly IOutputCacheStore _outputCache;

        public PagePublishingService(DataContext db, ITenantContext tenant, ISectionValidationService sectionValidation, IOutputCacheStore outputCache)
        {
            _db = db;
            _tenant = tenant;
            _sectionValidation = sectionValidation;
            _outputCache = outputCache;
        }


        public async Task<PublishResult> PublishAsync(
    int pageId,
    Guid actorUserId,
    CancellationToken ct = default)
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            try
            {
                // We'll return these out of the transaction so we can evict caches AFTER commit
                Guid? tenantIdForEvict = null;
                int? pageIdForEvict = null;
                int publishedRevisionIdForResult = 0;

                var result = await strategy.ExecuteAsync(async () =>
                {
                    if (!_tenant.IsResolved)
                        return PublishResult.Fail("Tenant not resolved.");

                    // Load page (tracked)
                    var page = await _db.Pages
                        .FirstOrDefaultAsync(p =>
                            p.Id == pageId &&
                            p.TenantId == _tenant.TenantId &&
                            !p.IsDeleted &&
                            p.IsActive, ct);

                    if (page == null)
                        return PublishResult.Fail("Page not found.");

                    if (page.DraftRevisionId == null)
                        return PublishResult.Fail("Cannot publish: no draft revision found.");

                    // Load draft revision (no tracking)
                    var draft = await _db.PageRevisions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r =>
                            r.Id == page.DraftRevisionId &&
                            r.TenantId == _tenant.TenantId &&
                            r.PageId == pageId &&
                            !r.IsDeleted &&
                            r.IsActive, ct);

                    if (draft == null)
                        return PublishResult.Fail("Cannot publish: draft revision not found.");

                    // Load draft sections (TRACKED)
                    var draftSections = await _db.PageRevisionSections
                        .Where(s =>
                            s.TenantId == _tenant.TenantId &&
                            s.PageRevisionId == page.DraftRevisionId.Value &&
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

                    await using var tx = await _db.Database.BeginTransactionAsync(ct);

                    // 1) Auto-migrate legacy gallery JSON (items -> images)
                    var anyMigrated = false;

                    foreach (var s in draftSections)
                    {
                        if (!sectionTypeKeys.TryGetValue(s.SectionTypeId, out var key) || string.IsNullOrWhiteSpace(key))
                            continue;

                        if (!string.Equals(key.Trim(), "gallery", StringComparison.OrdinalIgnoreCase))
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
                        var assets = await _db.MediaAssets
                            .AsNoTracking()
                            .Where(a =>
                                a.TenantId == _tenant.TenantId &&
                                !a.IsDeleted &&
                                a.IsActive &&
                                galleryAssetIds.Contains(a.Id))
                            .ToListAsync(ct);

                        var assetUrlMap = assets.ToDictionary(a => a.Id, a => BuildMediaUrl(a));

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
                                    continue;

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

                    // 2) Validate all sections
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
                    {
                        try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                        return new PublishResult { Success = false, Errors = errors };
                    }

                    // 3) Create published snapshot revision
                    var nextVersion =
                        (await _db.PageRevisions
                            .Where(r => r.TenantId == _tenant.TenantId &&
                                        r.PageId == pageId &&
                                        !r.IsDeleted)
                            .MaxAsync(r => (int?)r.VersionNumber, ct)
                        ?? 0) + 1;

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

                    // 4) Update page pointers + canonical columns
                    page.PublishedRevisionId = publishedRevision.Id;
                    page.PublishedAt = now;
                    page.PageStatusId = PageStatusIds.Published;

                    page.Title = publishedRevision.Title;
                    page.Slug = publishedRevision.Slug;
                    page.LayoutKey = publishedRevision.LayoutKey;
                    page.MetaTitle = publishedRevision.MetaTitle;
                    page.MetaDescription = publishedRevision.MetaDescription;
                    page.OgImageAssetId = publishedRevision.OgImageAssetId;

                    page.UpdatedAt = now;
                    page.UpdatedBy = actorUserId;

                    await _db.SaveChangesAsync(ct);

                    // 5) Create fresh draft cloned from published snapshot
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

                    // capture for post-commit cache eviction
                    tenantIdForEvict = page.TenantId;
                    pageIdForEvict = page.Id;
                    publishedRevisionIdForResult = publishedRevision.Id;

                    return PublishResult.Ok(publishedRevision.Id);
                });

                // ✅ Evict output cache AFTER commit (outside transaction)
                if (result.Success && tenantIdForEvict.HasValue && pageIdForEvict.HasValue)
                {
                    await _outputCache.EvictByTagAsync($"page:{pageIdForEvict.Value}", ct);
                    await _outputCache.EvictByTagAsync($"tenant:{tenantIdForEvict.Value}", ct);

                    // Optional global flush (usually unnecessary if tags are correct)
                    await _outputCache.EvictByTagAsync("public-pages", ct);
                }

                return result;
            }
            catch (Exception ex)
            {
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
