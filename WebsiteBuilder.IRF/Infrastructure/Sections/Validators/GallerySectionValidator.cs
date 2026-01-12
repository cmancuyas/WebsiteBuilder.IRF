using System.Text.Json;
using WebsiteBuilder.Models;
using WebsiteBuilder.IRF.Infrastructure.Sections;

namespace WebsiteBuilder.IRF.Infrastructure.Sections.Validators
{
    public sealed class GallerySectionValidator : ISectionContentValidator
    {
        public string TypeKey => "Gallery";

        public SectionValidationResult Validate(PageSection section)
        {
            if (!JsonValidationHelpers.TryParse(section.SettingsJson, out JsonDocument? doc, out var parseError))
                return SectionValidationResult.Fail(parseError!);

            using (doc!)
            {
                var root = doc.RootElement;
                var result = SectionValidationResult.Success();

                // Required: images (array, at least 1)
                if (!JsonValidationHelpers.HasArray(root, "images", out var arr))
                {
                    result.Add("Gallery requires 'images' (array).");
                    return result;
                }

                if (arr.GetArrayLength() == 0)
                {
                    result.Add("Gallery requires at least one image in 'images'.");
                    return result;
                }

                // Each item must have url (string, non-empty). alt optional (string if present)
                var i = 0;
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        result.Add($"Gallery images[{i}] must be an object.");
                        i++;
                        continue;
                    }

                    if (!JsonValidationHelpers.TryGetNonEmptyString(item, "url", out _))
                        result.Add($"Gallery images[{i}] requires 'url' (non-empty string).");

                    if (item.TryGetProperty("alt", out var alt) && alt.ValueKind != JsonValueKind.String)
                        result.Add($"Gallery images[{i}] optional 'alt' must be a string if provided.");

                    i++;
                }

                return result;
            }
        }
    }
}
