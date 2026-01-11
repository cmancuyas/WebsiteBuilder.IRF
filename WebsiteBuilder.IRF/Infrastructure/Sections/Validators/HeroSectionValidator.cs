using System.Text.Json;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Sections.Validators
{
    public sealed class HeroSectionValidator : ISectionContentValidator
    {
        public string TypeKey => "Hero";

        public SectionValidationResult Validate(PageSection section)
        {
            if (!JsonValidationHelpers.TryParse(section.ContentJson, out JsonDocument? doc, out var parseError))
                return SectionValidationResult.Fail(parseError!);

            using (doc!)
            {
                var root = doc.RootElement;
                var result = SectionValidationResult.Success();

                // Required: headline (string, non-empty)
                if (!JsonValidationHelpers.TryGetNonEmptyString(root, "headline", out _))
                    result.Add("Hero requires 'headline' (non-empty string).");

                // Optional: subheadline (string if present)
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
