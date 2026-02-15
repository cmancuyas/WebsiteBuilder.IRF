namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public sealed record ValidateSectionRequest
    {
        public string TypeKey { get; init; } = string.Empty;

        /// <summary>
        /// Raw JSON string for section settings (what your editor sends).
        /// </summary>
        public string? SettingsJson { get; init; }
    }
}
