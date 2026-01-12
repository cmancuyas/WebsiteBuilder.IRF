using System;
using System.Text.Json;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    internal static class JsonValidationHelpers
    {
        public static bool TryParse(string? json, out JsonDocument? doc, out string? error)
        {
            doc = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "SettingsJson is required.";
                return false;
            }

            try
            {
                doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    doc.Dispose();
                    doc = null;
                    error = "SettingsJson must be a JSON object (e.g., { ... }).";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Invalid JSON: {ex.Message}";
                return false;
            }
        }

        public static bool TryGetNonEmptyString(JsonElement root, string propName, out string value)
        {
            value = string.Empty;

            if (!root.TryGetProperty(propName, out var el))
                return false;

            if (el.ValueKind != JsonValueKind.String)
                return false;

            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return false;

            value = s;
            return true;
        }

        public static bool HasArray(JsonElement root, string propName, out JsonElement array)
        {
            array = default;

            if (!root.TryGetProperty(propName, out var el))
                return false;

            if (el.ValueKind != JsonValueKind.Array)
                return false;

            array = el;
            return true;
        }
    }
}
