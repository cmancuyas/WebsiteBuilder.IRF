using Microsoft.AspNetCore.Mvc;

namespace WebsiteBuilder.IRF.ViewComponents
{
    public sealed class PageSectionViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(int sectionTypeId, string? settingsJson)
        {
            // Shared section partials now accept JSON only: @model string?
            var viewPath = sectionTypeId switch
            {
                1 => "~/Pages/Shared/Sections/_Hero.cshtml",
                2 => "~/Pages/Shared/Sections/_Text.cshtml",
                3 => "~/Pages/Shared/Sections/_Gallery.cshtml",
                _ => "~/Pages/Shared/Sections/_Text.cshtml" // safe fallback
            };

            return View(viewPath, settingsJson);
        }
    }
}
