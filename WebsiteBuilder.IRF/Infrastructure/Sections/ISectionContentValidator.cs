namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public interface ISectionContentValidator
    {
        string TypeKey { get; }

        SectionValidationResult Validate(string? settingsJson);
    }
}
