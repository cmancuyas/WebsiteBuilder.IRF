using System.Text.Json;

namespace WebsiteBuilder.IRF.Infrastructure.Sections.Validators
{
    public sealed class HeroSectionValidator : ISectionContentValidator
    {
        public string TypeKey => "Hero";

        public SectionValidationResult Validate(string settingsJson)
        {
            var json = string.IsNullOrWhiteSpace(settingsJson) ? "{}" : settingsJson;

            if (!JsonValidationHelpers.TryParse(json, out JsonDocument? doc, out var parseError))
                return SectionValidationResult.Fail(parseError!);

            using (doc!)
            {
                var root = doc.RootElement;
                var result = SectionValidationResult.Success();

                if (!JsonValidationHelpers.TryGetNonEmptyString(root, "headline", out _))
                    result.Add("Hero requires 'headline' (non-empty string).");

                if (root.TryGetProperty("subheadline", out var sub) &&
                    sub.ValueKind != JsonValueKind.String)
                {
                    result.Add("Hero optional 'subheadline' must be a string if provided.");
                }

                return result;
            }
        }
    }
}
