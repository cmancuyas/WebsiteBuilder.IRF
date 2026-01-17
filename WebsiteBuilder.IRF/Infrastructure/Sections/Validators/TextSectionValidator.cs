using System.Text.Json;
using WebsiteBuilder.IRF.Infrastructure.Sections;

namespace WebsiteBuilder.IRF.Infrastructure.Sections.Validators
{
    public sealed class TextSectionValidator : ISectionContentValidator
    {
        public string TypeKey => "Text";

        public SectionValidationResult Validate(string settingsJson)
        {
            var json = string.IsNullOrWhiteSpace(settingsJson) ? "{}" : settingsJson;

            if (!JsonValidationHelpers.TryParse(json, out JsonDocument? doc, out var parseError))
                return SectionValidationResult.Fail(parseError!);

            using (doc!)
            {
                var root = doc.RootElement;
                var result = SectionValidationResult.Success();

                // Required: text (string, non-empty)
                if (!JsonValidationHelpers.TryGetNonEmptyString(root, "text", out _))
                    result.Add("Text requires 'text' (non-empty string).");

                return result;
            }
        }
    }
}
