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

        public SectionValidationService(
            ISectionRegistry registry,
            IEnumerable<ISectionContentValidator> validators)
        {
            _registry = registry;

            _validators = validators.ToDictionary(
                v => v.TypeKey,
                v => v,
                StringComparer.OrdinalIgnoreCase
            );
        }

        public SectionValidationResult Validate(PageSection section)
        {
            if (section == null)
                return SectionValidationResult.Fail("Section is required.");

            var typeKey = (section.TypeKey ?? string.Empty).Trim();

            // Ensure the section type exists
            if (!_registry.TryGet(typeKey, out _))
                return SectionValidationResult.Fail($"Unknown section type '{typeKey}'.");

            // Ensure a validator exists for the type
            if (!_validators.TryGetValue(typeKey, out var validator))
                return SectionValidationResult.Fail($"No validator registered for section type '{typeKey}'.");

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

            return Task.FromResult(Validate(section));
        }
    }
}
