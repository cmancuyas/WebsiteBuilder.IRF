using System.ComponentModel.DataAnnotations;

namespace WebsiteBuilder.IRF.ViewModels.Admin.Pages
{
    public sealed class PageSectionReorderVm
    {
        [Required]
        public List<int> OrderedIds { get; set; } = new();
    }
}
