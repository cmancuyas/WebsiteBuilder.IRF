namespace WebsiteBuilder.IRF.ViewModels.Admin.Navigation
{
    public sealed class NavSaveRequestVm
    {
        public int MenuId { get; set; }
        public List<NavSaveItemVm> Items { get; set; } = new();
    }
}
