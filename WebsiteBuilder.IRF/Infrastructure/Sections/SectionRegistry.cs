using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public sealed record SectionDefinition(
        string TypeKey,
        string DisplayName,
        string PartialViewPath
    );

    public interface ISectionRegistry
    {
        IReadOnlyCollection<SectionDefinition> All { get; }
        bool TryGet(string? typeKey, out SectionDefinition definition);
    }

    public sealed class SectionRegistry : ISectionRegistry
    {
        // Central registry of allowed/known section types.
        // TypeKey must match PageSection.TypeKey values in DB.
        private static readonly IReadOnlyDictionary<string, SectionDefinition> _map =
            new Dictionary<string, SectionDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["Hero"] = new SectionDefinition(
                    "Hero",
                    "Hero Banner",
                    "Shared/Sections/_Hero"
                ),
                ["Text"] = new SectionDefinition(
                    "Text",
                    "Text Block",
                    "Shared/Sections/_Text"
                ),
                ["Gallery"] = new SectionDefinition(
                    "Gallery",
                    "Image Gallery",
                    "Shared/Sections/_Gallery"
                ),
            };


        public IReadOnlyCollection<SectionDefinition> All => _map.Values.ToList();

        public bool TryGet(string? typeKey, out SectionDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(typeKey))
            {
                definition = default!;
                return false;
            }

            return _map.TryGetValue(typeKey.Trim(), out definition!);
        }
    }

    public static class SectionRegistryExtensions
    {
        /// <summary>
        /// Normalizes null/empty/whitespace TypeKey to empty and trims.
        /// </summary>
        public static string NormalizeTypeKey(this PageSection section)
            => (section.TypeKey ?? string.Empty).Trim();
    }
}
