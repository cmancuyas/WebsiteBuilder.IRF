using System.Text.Json;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public interface ISectionJsonValidator
    {
        bool Validate(string typeKey, string json, out string error);
    }

    public sealed class SectionJsonValidator : ISectionJsonValidator
    {
        public bool Validate(string typeKey, string json, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(typeKey))
            {
                error = "TypeKey is required.";
                return false;
            }

            json = (json ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "ContentJson is required.";
                return false;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (Exception)
            {
                error = "ContentJson must be valid JSON.";
                return false;
            }

            // Require object at top-level for these sections (you can expand later)
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "ContentJson must be a JSON object (e.g., { ... }).";
                return false;
            }

            // Per-section rules
            if (typeKey.Equals("Hero", StringComparison.OrdinalIgnoreCase))
                return ValidateHero(doc, out error);

            if (typeKey.Equals("Text", StringComparison.OrdinalIgnoreCase))
                return ValidateText(doc, out error);

            if (typeKey.Equals("Gallery", StringComparison.OrdinalIgnoreCase))
                return ValidateGallery(doc, out error);

            // Unknown types: allow object (or flip this to false if you want strict enforcement)
            return true;
        }

        private static bool ValidateHero(JsonDocument doc, out string error)
        {
            error = string.Empty;

            // Optional fields, but if present must be string
            if (doc.RootElement.TryGetProperty("headline", out var headline) &&
                headline.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                error = "Hero.headline must be a string.";
                return false;
            }

            if (doc.RootElement.TryGetProperty("subheadline", out var subheadline) &&
                subheadline.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                error = "Hero.subheadline must be a string.";
                return false;
            }

            // If you want at least one of them required:
            // var hasHeadline = doc.RootElement.TryGetProperty("headline", out var h) && h.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(h.GetString());
            // var hasSub = doc.RootElement.TryGetProperty("subheadline", out var s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString());
            // if (!hasHeadline && !hasSub) { error = "Hero requires headline or subheadline."; return false; }

            return true;
        }

        private static bool ValidateText(JsonDocument doc, out string error)
        {
            error = string.Empty;

            // Require "text" string (your _Text.cshtml reads "text")
            if (!doc.RootElement.TryGetProperty("text", out var text))
            {
                error = "Text section requires property: text.";
                return false;
            }

            if (text.ValueKind != JsonValueKind.String)
            {
                error = "Text.text must be a string.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(text.GetString()))
            {
                error = "Text.text cannot be empty.";
                return false;
            }

            return true;
        }

        private static bool ValidateGallery(JsonDocument doc, out string error)
        {
            error = string.Empty;

            // Require images: [ { url: string, alt?: string } ]
            if (!doc.RootElement.TryGetProperty("images", out var images))
            {
                error = "Gallery section requires property: images.";
                return false;
            }

            if (images.ValueKind != JsonValueKind.Array)
            {
                error = "Gallery.images must be an array.";
                return false;
            }

            foreach (var item in images.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    error = "Each Gallery.images item must be an object.";
                    return false;
                }

                if (!item.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
                {
                    error = "Each Gallery.images item must include url (string).";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(url.GetString()))
                {
                    error = "Gallery.images.url cannot be empty.";
                    return false;
                }

                if (item.TryGetProperty("alt", out var alt) &&
                    alt.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    error = "Gallery.images.alt must be a string.";
                    return false;
                }
            }

            return true;
        }
    }
}
