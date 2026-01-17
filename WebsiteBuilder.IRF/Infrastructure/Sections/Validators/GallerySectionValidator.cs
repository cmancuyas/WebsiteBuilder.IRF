using System.Text.Json;
using WebsiteBuilder.IRF.Infrastructure.Sections;

namespace WebsiteBuilder.IRF.Infrastructure.Sections.Validators
{
    public sealed class GallerySectionValidator : ISectionContentValidator
    {
        public string TypeKey => "Gallery";

        private const int MaxImages = 30;
        private const int MaxAltLength = 200;
        private const int MaxCaptionLength = 500;

        public SectionValidationResult Validate(string settingsJson)
        {
            var json = string.IsNullOrWhiteSpace(settingsJson) ? "{}" : settingsJson;

            if (!JsonValidationHelpers.TryParse(json, out JsonDocument? doc, out var parseError))
                return SectionValidationResult.Fail(parseError!);

            using (doc!)
            {
                var root = doc.RootElement;
                var result = SectionValidationResult.Success();

                if (!JsonValidationHelpers.HasArray(root, "images", out var arr))
                {
                    result.Add("Gallery requires 'images' (array).");
                    return result;
                }

                var count = arr.GetArrayLength();
                if (count == 0)
                {
                    result.Add("Gallery requires at least one image in 'images'.");
                    return result;
                }

                if (count > MaxImages)
                    result.Add($"Gallery cannot exceed {MaxImages} images.");

                if (root.TryGetProperty("layout", out var layout) &&
                    layout.ValueKind != JsonValueKind.String)
                {
                    result.Add("Gallery optional 'layout' must be a string if provided.");
                }

                if (root.TryGetProperty("columns", out var cols))
                {
                    if (cols.ValueKind != JsonValueKind.Number)
                    {
                        result.Add("Gallery optional 'columns' must be a number if provided.");
                    }
                    else
                    {
                        var c = cols.GetInt32();
                        if (c < 1 || c > 6)
                            result.Add("Gallery optional 'columns' must be between 1 and 6.");
                    }
                }

                var i = 0;
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        result.Add($"Gallery images[{i}] must be an object.");
                        i++;
                        continue;
                    }

                    if (!JsonValidationHelpers.TryGetNonEmptyString(item, "url", out var url))
                    {
                        result.Add($"Gallery images[{i}] requires 'url' (non-empty string).");
                    }
                    else if (!IsValidUrl(url))
                    {
                        result.Add($"Gallery images[{i}] 'url' must be a valid http/https URL or app-relative '/'.");
                    }

                    if (item.TryGetProperty("alt", out var alt))
                    {
                        if (alt.ValueKind != JsonValueKind.String)
                            result.Add($"Gallery images[{i}] optional 'alt' must be a string.");
                        else if ((alt.GetString() ?? string.Empty).Length > MaxAltLength)
                            result.Add($"Gallery images[{i}] optional 'alt' cannot exceed {MaxAltLength} characters.");
                    }

                    if (item.TryGetProperty("caption", out var caption))
                    {
                        if (caption.ValueKind != JsonValueKind.String)
                            result.Add($"Gallery images[{i}] optional 'caption' must be a string.");
                        else if ((caption.GetString() ?? string.Empty).Length > MaxCaptionLength)
                            result.Add($"Gallery images[{i}] optional 'caption' cannot exceed {MaxCaptionLength} characters.");
                    }

                    i++;
                }

                return result;
            }
        }

        private static bool IsValidUrl(string url)
        {
            url = url.Trim();

            if (url.StartsWith("/", StringComparison.Ordinal))
                return true;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

            return false;
        }
    }
}
