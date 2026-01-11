using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public interface ISectionContentValidator
    {
        string TypeKey { get; }
        SectionValidationResult Validate(PageSection section);
    }
}
