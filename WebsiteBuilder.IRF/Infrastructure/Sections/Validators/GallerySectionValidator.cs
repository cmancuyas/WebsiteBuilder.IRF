using System.Text.Json;
using WebsiteBuilder.Models;
using WebsiteBuilder.IRF.Infrastructure.Sections;

namespace WebsiteBuilder.IRF.Infrastructure.Sections.Validators
{
    public sealed class GallerySectionValidator : ISectionContentValidator
    {
        public string TypeKey => "Gallery";

        // Reasonable limits to protect your DB and UI
        private const int MaxImages = 30;
        private const int MaxAltLength = 200;
        private const int MaxCaptionLength = 500;

        public SectionValidationResult Validate(PageSection section)
        {
            if (!JsonValidationHelpers.TryParse(section.SettingsJson, out JsonDocument? doc, out var parseError))
                return SectionValidationResult.Fail(parseError!);

            using (doc!)
            {
                var root = doc!.RootElement;
                var result = SectionValidationResult.Success();

                // Required: images (array, at least 1)
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
                {
                    result.Add($"Gallery cannot exceed {MaxImages} images.");
                }

                // Optional: layout (string)
                if (root.TryGetProperty("layout", out var layout) && layout.ValueKind != JsonValueKind.String)
                {
                    result.Add("Gallery optional 'layout' must be a string if provided (e.g., 'grid').");
                }

                // Optional: columns (number 1..6)
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

                // Each item must be object with:
                // - url (required, non-empty string, valid absolute http/https OR app-relative '/...')
                // - alt (optional string)
                // - caption (optional string)
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
                    else
                    {
                        if (!IsValidUrl(url))
                            result.Add($"Gallery images[{i}] 'url' must be a valid http/https URL or an app-relative path starting with '/'.");
                    }

                    if (item.TryGetProperty("alt", out var alt))
                    {
                        if (alt.ValueKind != JsonValueKind.String)
                        {
                            result.Add($"Gallery images[{i}] optional 'alt' must be a string if provided.");
                        }
                        else if ((alt.GetString() ?? string.Empty).Length > MaxAltLength)
                        {
                            result.Add($"Gallery images[{i}] optional 'alt' cannot exceed {MaxAltLength} characters.");
                        }
                    }

                    if (item.TryGetProperty("caption", out var caption))
                    {
                        if (caption.ValueKind != JsonValueKind.String)
                        {
                            result.Add($"Gallery images[{i}] optional 'caption' must be a string if provided.");
                        }
                        else if ((caption.GetString() ?? string.Empty).Length > MaxCaptionLength)
                        {
                            result.Add($"Gallery images[{i}] optional 'caption' cannot exceed {MaxCaptionLength} characters.");
                        }
                    }

                    i++;
                }

                return result;
            }
        }

        private static bool IsValidUrl(string url)
        {
            url = url.Trim();

            // Allow app-relative URLs (recommended for your MediaAsset routes)
            if (url.StartsWith("/", StringComparison.Ordinal))
                return true;

            // Allow absolute http/https
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }
    }
}
