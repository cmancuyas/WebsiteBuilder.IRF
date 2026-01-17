using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class SectionRowRenderVm
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        public string CollapseId { get; init; } = "";
        public string EditorPartialPath { get; init; } = "";
        public bool IsEditable { get; init; }

        // Optional but useful
        public PageRevisionSection Section { get; init; } = default!;
    }

}
