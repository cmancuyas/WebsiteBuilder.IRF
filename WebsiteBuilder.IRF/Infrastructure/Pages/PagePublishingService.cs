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
            var page = await _db.Pages
                .Include(p => p.Sections)
                .FirstOrDefaultAsync(p =>
                    p.Id == pageId &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted, ct);

            if (page == null)
                return PublishResult.Fail("Page not found.");

            var sections = page.Sections
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.SortOrder)
                .ToList();

            if (sections.Count == 0)
                return PublishResult.Fail("Cannot publish: this page has no sections.");

            // Resolve SectionTypeId -> Name once
            var sectionTypeIds = sections.Select(s => s.SectionTypeId).Distinct().ToList();

            var sectionTypeNames = await _db.SectionTypes
                .Where(st => sectionTypeIds.Contains(st.Id))
                .ToDictionaryAsync(st => st.Id, st => st.Name, ct);

            var errors = new List<string>();

            foreach (var s in sections)
            {
                var typeKey = sectionTypeNames.TryGetValue(s.SectionTypeId, out var name)
                    ? name
                    : s.SectionTypeId.ToString(); // fallback only

                // Validate SettingsJson payload (service-layer hard rule)
                var validation = await _sectionValidation.ValidateAsync(typeKey, s.SettingsJson);

                if (!validation.IsValid)
                    errors.AddRange(validation.Errors.Select(e => $"Section '{typeKey}': {e}"));
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

                var revision = new PageRevision
                {
                    TenantId = _tenant.TenantId,
                    PageId = pageId,
                    VersionNumber = nextVersion,
                    IsPublishedSnapshot = true,

                    Title = page.Title,
                    Slug = page.Slug,
                    LayoutKey = page.LayoutKey ?? string.Empty,
                    MetaTitle = page.MetaTitle ?? string.Empty,
                    MetaDescription = page.MetaDescription ?? string.Empty,
                    OgImageAssetId = page.OgImageAssetId,
                    PublishedAt = now,

                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = now,
                    CreatedBy = actorUserId
                };

                foreach (var s in sections)
                {
                    revision.Sections.Add(new PageRevisionSection
                    {
                        TenantId = _tenant.TenantId,
                        SourcePageSectionId = s.Id,

                        SectionTypeId = s.SectionTypeId,
                        SortOrder = s.SortOrder,
                        SettingsJson = s.SettingsJson,

                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        CreatedBy = actorUserId
                    });
                }

                // 1) Save revision first to get real revision.Id
                _db.PageRevisions.Add(revision);
                await _db.SaveChangesAsync(ct);

                // 2) Update canonical publish pointer on Page (now revision.Id is real)
                page.PublishedRevisionId = revision.Id;
                page.PublishedAt = revision.PublishedAt;
                page.PageStatusId = PageStatusIds.Published;
                page.UpdatedAt = now;
                page.UpdatedBy = actorUserId;

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return PublishResult.Ok(revision.Id);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
    }
}
