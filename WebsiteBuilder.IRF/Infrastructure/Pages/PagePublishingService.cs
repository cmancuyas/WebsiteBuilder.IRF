using Microsoft.EntityFrameworkCore;
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
            // Load page + draft pointer (do NOT include legacy page.Sections)
            var page = await _db.Pages
                .FirstOrDefaultAsync(p =>
                    p.Id == pageId &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return PublishResult.Fail("Page not found.");

            if (page.DraftRevisionId == null)
                return PublishResult.Fail("Cannot publish: no draft revision found.");

            // Load draft revision (title/slug/meta snapshot comes from revision)
            var draft = await _db.PageRevisions
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == page.DraftRevisionId &&
                    r.TenantId == _tenant.TenantId &&
                    r.PageId == pageId &&
                    !r.IsDeleted, ct);

            if (draft == null)
                return PublishResult.Fail("Cannot publish: draft revision not found.");

            // Load draft sections
            var draftSections = await _db.PageRevisionSections
                .AsNoTracking()
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

            // Resolve SectionTypeId -> Key once (do NOT use Name)
            var sectionTypeIds = draftSections.Select(s => s.SectionTypeId).Distinct().ToList();

            var sectionTypeKeys = await _db.SectionTypes
                .AsNoTracking()
                .Where(st => sectionTypeIds.Contains(st.Id) && st.IsActive && !st.IsDeleted)
                .ToDictionaryAsync(st => st.Id, st => st.Key, ct);

            // Validate all sections
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
                {
                    // validation.Errors is a list of strings in your result type
                    errors.AddRange(validation.Errors.Select(e => $"Section '{typeKey}': {e}"));
                }
            }

            if (errors.Count > 0)
                return new PublishResult { Success = false, Errors = errors };

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var nextVersion =
                    (await _db.PageRevisions
                        .Where(r => r.TenantId == _tenant.TenantId &&
                                    r.PageId == pageId &&
                                    !r.IsDeleted)
                        .MaxAsync(r => (int?)r.VersionNumber, ct)
                    ?? 0) + 1;

                var now = DateTime.UtcNow;

                // 1) Create published snapshot revision from draft content
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
                        // keep provenance if present; otherwise leave null
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

                // 2) Update canonical publish pointer on Page
                page.PublishedRevisionId = publishedRevision.Id;
                page.PublishedAt = now;
                page.PageStatusId = PageStatusIds.Published;
                page.UpdatedAt = now;
                page.UpdatedBy = actorUserId;

                await _db.SaveChangesAsync(ct);

                // 3) Create a fresh draft revision cloned from the published snapshot
                var newDraft = new PageRevision
                {
                    TenantId = _tenant.TenantId,
                    PageId = pageId,
                    VersionNumber = publishedRevision.VersionNumber, // keep same number for draft lineage (optional)
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
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

    }
}
