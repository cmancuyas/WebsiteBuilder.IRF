using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public interface ISectionValidationService
    {
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

            _validators = validators
                .GroupBy(v => (v.TypeKey ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public Task<SectionValidationResult> ValidateAsync(string typeKey, string? settingsJson)
        {
            var normalizedTypeKey = (typeKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTypeKey))
                return Task.FromResult(
                    SectionValidationResult.Fail("Section type is required.")
                );

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

            // ✅ Validate JSON only (no PageSection)
            return validator.Validate(json);
        }
    }
}
