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
        Task<SectionValidationResult> ValidateAsync(string typeKey, string? contentJson);
    }

    public sealed class SectionValidationService : ISectionValidationService
    {
        private readonly ISectionRegistry _registry;
        private readonly IReadOnlyDictionary<string, ISectionContentValidator> _validators;

        // Choose policy:
        // true  => missing validator is OK (section passes)
        // false => missing validator fails (enforce validators for every type)
        private const bool AllowMissingValidator = true;

        public SectionValidationService(
            ISectionRegistry registry,
            IEnumerable<ISectionContentValidator> validators)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            if (validators == null) throw new ArgumentNullException(nameof(validators));

            // Avoid ToDictionary duplicate-key runtime crash
            _validators = validators
                .GroupBy(v => (v.TypeKey ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public SectionValidationResult Validate(PageSection section)
        {
            if (section == null)
                return SectionValidationResult.Fail("Section is required.");

            var typeKey = (section.TypeKey ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(typeKey))
                return SectionValidationResult.Fail("Section type is required.");

            // Ensure the section type exists in registry
            if (!_registry.TryGet(typeKey, out _))
                return SectionValidationResult.Fail($"Unknown section type '{typeKey}'.");

            // If no validator registered, decide policy
            if (!_validators.TryGetValue(typeKey, out var validator))
            {
                return AllowMissingValidator
                    ? SectionValidationResult.Success()
                    : SectionValidationResult.Fail($"No validator registered for section type '{typeKey}'.");
            }

            // Delegate validation to the section-specific validator
            return validator.Validate(section);
        }

        public Task<SectionValidationResult> ValidateAsync(string typeKey, string? contentJson)
        {
            var section = new PageSection
            {
                TypeKey = (typeKey ?? string.Empty).Trim(),
                ContentJson = contentJson
            };

            // This keeps the async signature (for your AJAX endpoint),
            // while still using the synchronous per-section validators you currently have.
            return Task.FromResult(Validate(section));
        }
    }
}
