using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.IRF.DataAccess;
using WebsiteBuilder.IRF.Infrastructure.Tenancy;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    /// <summary>
    /// Validates a page is publishable based on the *draft revision* sections,
    /// not legacy PageSections.
    /// </summary>
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

        public sealed class PagePublishValidationResult
        {
            public bool IsValid => Errors.Count == 0;
            public List<PageSectionPublishError> Errors { get; } = new();
        }

        // ✅ Matches what Edit.cshtml.cs expects
        public sealed class PageSectionPublishError
        {
            public int SectionId { get; init; }
            public string TypeKey { get; init; } = "General";
            public List<string> Messages { get; init; } = new();
        }

        /// <summary>
        /// Called by Edit.cshtml.cs: validates draft revision sections for the given page.
        /// </summary>
        public async Task<PagePublishValidationResult> ValidateDraftSectionsAsync(
            int pageId,
            CancellationToken ct = default)
        {
            var result = new PagePublishValidationResult();

            if (!_tenant.IsResolved)
            {
                AddGeneralError(result, "Tenant not resolved.");
                return result;
            }

            var page = await _db.Pages
                .AsNoTracking()
                .Where(p =>
                    p.Id == pageId &&
                    p.TenantId == _tenant.TenantId &&
                    !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (page == null)
            {
                AddGeneralError(result, "Page not found.");
                return result;
            }

            // Resolve draft revision id (prefer property if present; fallback to latest revision for this page)
            var draftRevisionId = TryGetDraftRevisionId(page);

            if (draftRevisionId == null)
            {
                draftRevisionId = await _db.PageRevisions
                    .AsNoTracking()
                    .Where(r =>
                        r.PageId == page.Id &&
                        r.TenantId == _tenant.TenantId &&
                        !r.IsDeleted)
                    .OrderByDescending(r => r.Id)
                    .Select(r => (int?)r.Id)
                    .FirstOrDefaultAsync(ct);
            }

            if (draftRevisionId == null)
            {
                AddGeneralError(result, "No draft revision exists for this page.");
                return result;
            }

            // ✅ Read from PageRevisionSections (SettingsJson + SectionType)
            // Only validate NON-deleted sections so Trash doesn't block Publish.
            var sections = await _db.PageRevisionSections
                .AsNoTracking()
                .Include(s => s.SectionType)
                .Where(s =>
                    s.TenantId == _tenant.TenantId &&
                    s.PageRevisionId == draftRevisionId.Value &&
                    !s.IsDeleted &&
                    s.IsActive) // keep if you use IsActive as "disabled"
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToListAsync(ct);

            if (sections.Count == 0)
            {
                AddGeneralError(result, "Cannot publish a page with no sections.");
                return result;
            }

            foreach (var s in sections)
            {
                var typeKey = (s.SectionType?.Key ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(typeKey))
                {
                    AddSectionError(
                        result,
                        sectionId: s.Id,
                        typeKey: "Unknown",
                        message: "Section type is missing."
                    );
                    continue;
                }

                var settingsJson = string.IsNullOrWhiteSpace(s.SettingsJson) ? "{}" : s.SettingsJson;

                var vr = await _sectionValidation.ValidateAsync(typeKey, settingsJson);

                if (!vr.IsValid)
                {
                    AddSectionErrors(
                        result,
                        sectionId: s.Id,
                        typeKey: typeKey,
                        messages: vr.Errors
                    );
                }
            }

            return result;
        }

        private static void AddGeneralError(PagePublishValidationResult result, string message)
            => AddSectionError(result, sectionId: 0, typeKey: "General", message: message);

        private static void AddSectionError(PagePublishValidationResult result, int sectionId, string typeKey, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var existing = result.Errors.FirstOrDefault(e => e.SectionId == sectionId && e.TypeKey == typeKey);
            if (existing == null)
            {
                result.Errors.Add(new PageSectionPublishError
                {
                    SectionId = sectionId,
                    TypeKey = typeKey,
                    Messages = new List<string> { message }
                });
            }
            else
            {
                existing.Messages.Add(message);
            }
        }

        private static void AddSectionErrors(PagePublishValidationResult result, int sectionId, string typeKey, IEnumerable<string> messages)
        {
            if (messages == null) return;
            foreach (var m in messages)
                AddSectionError(result, sectionId, typeKey, m);
        }

        private static int? TryGetDraftRevisionId(object page)
        {
            var t = page.GetType();

            foreach (var name in new[] { "DraftRevisionId", "WorkingRevisionId", "CurrentRevisionId" })
            {
                var prop = t.GetProperty(name);
                if (prop == null) continue;

                var val = prop.GetValue(page);
                if (val == null) continue;

                // boxed int? with value becomes boxed int, so this covers both int and int?
                if (val is int i) return i;
            }

            return null;
        }
    }
}
