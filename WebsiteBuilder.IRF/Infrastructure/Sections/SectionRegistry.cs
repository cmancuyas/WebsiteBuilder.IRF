using System.Collections.ObjectModel;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public sealed record SectionDefinition(
        string TypeKey,
        string DisplayName,
        string PartialViewPath,
        string DefaultJson
    );

    public interface ISectionRegistry
    {
        IReadOnlyCollection<SectionDefinition> All { get; }
        bool TryGet(string? typeKey, out SectionDefinition definition);

        /// <summary>
        /// Returns canonical TypeKey (e.g., "Hero") for any casing/alias; null if unknown.
        /// </summary>
        string? Canonicalize(string? typeKey);
    }

    public sealed class SectionRegistry : ISectionRegistry
    {
        private readonly IReadOnlyCollection<SectionDefinition> _all;
        private readonly Dictionary<string, SectionDefinition> _map;

        public IReadOnlyCollection<SectionDefinition> All => _all;

        public SectionRegistry()
        {
            // IMPORTANT:
            // - Use canonical TypeKey values that match what your runtime page switch expects: "Hero", "Text", "Gallery".
            // - Dictionary is case-insensitive so "hero", "HERO" all resolve to "Hero".
            _map = new Dictionary<string, SectionDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["Hero"] = new SectionDefinition(
                    TypeKey: "Hero",
                    DisplayName: "Hero",
                    PartialViewPath: "Shared/Sections/_Hero",
                    DefaultJson: """{"headline":"Welcome","subheadline":"Your tagline here"}"""
                ),

                ["Text"] = new SectionDefinition(
                    TypeKey: "Text",
                    DisplayName: "Text",
                    PartialViewPath: "Shared/Sections/_Text",
                    DefaultJson: """{"text":"Your content here."}"""
                ),

                // THIS is where you place your line:
                // ["Gallery"] = new SectionDefinition("Gallery", "Image Gallery", "Shared/Sections/_Gallery", ...);
                ["Gallery"] = new SectionDefinition(
                    TypeKey: "Gallery",
                    DisplayName: "Image Gallery",
                    PartialViewPath: "Shared/Sections/_Gallery",
                    DefaultJson: """{"images":[{"url":"https://example.com/image.jpg","alt":"Sample image"}]}"""
                ),
            };

            _all = new ReadOnlyCollection<SectionDefinition>(_map.Values
                .OrderBy(x => x.DisplayName)
                .ToList());
        }

        public bool TryGet(string? typeKey, out SectionDefinition definition)
        {
            definition = default!;
            if (string.IsNullOrWhiteSpace(typeKey))
                return false;

            return _map.TryGetValue(typeKey.Trim(), out definition!);
        }

        public string? Canonicalize(string? typeKey)
        {
            if (!TryGet(typeKey, out var def))
                return null;

            return def.TypeKey; // canonical
        }
    }
}
