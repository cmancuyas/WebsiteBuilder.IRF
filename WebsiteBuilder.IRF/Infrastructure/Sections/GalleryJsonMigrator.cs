using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public static class GalleryJsonMigrator
    {
        // Returns true only when it produced a *different* JSON payload.
        public static bool TryMigrateLegacyItemsToImages(
            string? contentJson,
            out string migratedJson,
            out string? note)
        {
            migratedJson = contentJson ?? string.Empty;
            note = null;

            if (string.IsNullOrWhiteSpace(contentJson))
                return false;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(contentJson);
            }
            catch
            {
                // Leave invalid JSON to existing validation (do not “fix” corruption here).
                return false;
            }

            if (root is not JsonObject obj)
                return false;

            // If images exists and has entries, no migration.
            if (obj.TryGetPropertyValue("images", out var imagesNode) &&
                imagesNode is JsonArray imagesArr &&
                imagesArr.Count > 0)
            {
                return false;
            }

            // If no legacy items, nothing to migrate.
            if (!obj.TryGetPropertyValue("items", out var itemsNode) || itemsNode is not JsonArray legacyItems)
                return false;

            var newImages = new JsonArray();

            foreach (var item in legacyItems)
            {
                // Legacy could be a number (assetId), string, or object.
                if (item is JsonValue v)
                {
                    // number or string
                    if (TryReadInt(v, out var id) && id > 0)
                    {
                        newImages.Add(new JsonObject
                        {
                            ["assetId"] = id
                        });
                    }

                    continue;
                }

                if (item is JsonObject itemObj)
                {
                    // Try common keys
                    var id = ReadInt(itemObj, "assetId")
                          ?? ReadInt(itemObj, "id")
                          ?? ReadInt(itemObj, "mediaAssetId")
                          ?? ReadInt(itemObj, "imageId");

                    if (id is null || id <= 0)
                        continue;

                    var img = new JsonObject
                    {
                        ["assetId"] = id.Value
                    };

                    // Optional carry-over fields (best-effort)
                    var alt = ReadString(itemObj, "alt") ?? ReadString(itemObj, "altText");
                    var caption = ReadString(itemObj, "caption") ?? ReadString(itemObj, "title");

                    if (!string.IsNullOrWhiteSpace(alt)) img["alt"] = alt!;
                    if (!string.IsNullOrWhiteSpace(caption)) img["caption"] = caption!;

                    newImages.Add(img);
                }
            }

            // If nothing usable, do not overwrite; validator will block publish.
            if (newImages.Count == 0)
                return false;

            // Write back as images, and remove legacy items (clean migration).
            obj["images"] = newImages;
            obj.Remove("items");

            migratedJson = obj.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            });

            note = "Migrated legacy gallery schema: items -> images.";
            return !string.Equals(migratedJson, contentJson, StringComparison.Ordinal);
        }

        private static bool TryReadInt(JsonValue v, out int value)
        {
            value = 0;
            try
            {
                // Handles both number and string cases.
                if (v.TryGetValue<int>(out var i))
                {
                    value = i;
                    return true;
                }

                if (v.TryGetValue<string>(out var s) && int.TryParse(s, out i))
                {
                    value = i;
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static int? ReadInt(JsonObject obj, string key)
        {
            if (!obj.TryGetPropertyValue(key, out var node) || node is null)
                return null;

            if (node is JsonValue v && TryReadInt(v, out var i))
                return i;

            return null;
        }

        private static string? ReadString(JsonObject obj, string key)
        {
            if (!obj.TryGetPropertyValue(key, out var node) || node is null)
                return null;

            try
            {
                return node.GetValue<string>();
            }
            catch
            {
                return null;
            }
        }
    }
}
