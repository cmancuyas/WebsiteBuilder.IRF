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
                error = "Section type is required.";
                return false;
            }

            json = (json ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                // Canonical payload name
                error = "SettingsJson is required.";
                return false;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch
            {
                error = "SettingsJson must be valid JSON.";
                return false;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "SettingsJson must be a JSON object (e.g., { ... }).";
                return false;
            }

            if (typeKey.Equals("Hero", StringComparison.OrdinalIgnoreCase))
                return ValidateHero(doc, out error);

            if (typeKey.Equals("Text", StringComparison.OrdinalIgnoreCase))
                return ValidateText(doc, out error);

            if (typeKey.Equals("Gallery", StringComparison.OrdinalIgnoreCase))
                return ValidateGallery(doc, out error);

            // Unknown types: allow object (or change to false for strict enforcement)
            return true;
        }

        private static bool ValidateHero(JsonDocument doc, out string error)
        {
            error = string.Empty;

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

            return true;
        }

        private static bool ValidateText(JsonDocument doc, out string error)
        {
            error = string.Empty;

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
