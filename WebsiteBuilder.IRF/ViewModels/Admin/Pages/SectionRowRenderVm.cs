using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages;

public sealed class SectionRowRenderVm
{
    // This must be PageRevisionSection.Id
    public int RevisionSectionId { get; init; }

    // Template/type id can be useful too (optional)
    public int SectionTypeId { get; init; }

    public string Title { get; init; } = "";
    public string CollapseId { get; init; } = "";
    public string EditorPartialPath { get; init; } = "";
    public bool IsEditable { get; init; }

    // ✅ NEW: section-level concurrency token (base64 rowversion)
    public string SectionRowVersionBase64 { get; init; } = "";

    public PageRevisionSection Section { get; init; } = default!;
}
