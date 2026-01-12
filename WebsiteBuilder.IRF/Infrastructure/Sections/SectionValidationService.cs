using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public interface ISectionValidationService
    {
        SectionValidationResult Validate(PageSection section);

        // Used by live JSON validation endpoint (AJAX)
        Task<SectionValidationResult> ValidateAsync(string typeKey, string? settingsJson);
    }

    public sealed class SectionValidationService : ISectionValidationService
    {
        private readonly ISectionRegistry _registry;
        private readonly IReadOnlyDictionary<string, ISectionContentValidator> _validators;

        // Policy:
        // true  => unknown type or missing validator is allowed (passes)
        // false => unknown type or missing validator fails
        private const bool AllowMissingValidator = true;

        public SectionValidationService(
            ISectionRegistry registry,
            IEnumerable<ISectionContentValidator> validators)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (validators == null) throw new ArgumentNullException(nameof(validators));

            // Avoid duplicate key exceptions by grouping
            _validators = validators
                .GroupBy(v => (v.TypeKey ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public SectionValidationResult Validate(PageSection section)
        {
            if (section == null)
                return SectionValidationResult.Fail("Section is required.");

            // ✅ Canonical type resolution: SectionType.Name is authoritative
            var typeKey = (section.SectionType?.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(typeKey))
            {
                // If SectionType navigation isn't loaded, the caller must provide it or validate via ValidateAsync.
                // We fail here to avoid silently validating against an unknown type.
                return SectionValidationResult.Fail("Section type is required.");
            }

            // ✅ Canonical payload: SettingsJson
            var json = string.IsNullOrWhiteSpace(section.SettingsJson) ? "{}" : section.SettingsJson;

            return ValidateCore(typeKey, json);
        }

        public Task<SectionValidationResult> ValidateAsync(string typeKey, string? settingsJson)
        {
            var normalizedTypeKey = (typeKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTypeKey))
                return Task.FromResult(SectionValidationResult.Fail("Section type is required."));

            var json = string.IsNullOrWhiteSpace(settingsJson) ? "{}" : settingsJson;

            return Task.FromResult(ValidateCore(normalizedTypeKey, json));
        }

        private SectionValidationResult ValidateCore(string typeKey, string json)
        {
            // Registry existence check
            if (!_registry.TryGet(typeKey, out _))
            {
                return AllowMissingValidator
                    ? SectionValidationResult.Success()
                    : SectionValidationResult.Fail($"Unknown section type '{typeKey}'.");
            }

            // Validator lookup
            if (!_validators.TryGetValue(typeKey, out var validator))
            {
                return AllowMissingValidator
                    ? SectionValidationResult.Success()
                    : SectionValidationResult.Fail($"No validator registered for section type '{typeKey}'.");
            }

            // ✅ Canonical section object for validators (SettingsJson only)
            var canonical = new PageSection
            {
                SettingsJson = json,
                // SectionTypeId/SectionType are not required for content validation,
                // but can be populated by caller if a validator needs it in the future.
            };

            return validator.Validate(canonical);
        }
    }
}
