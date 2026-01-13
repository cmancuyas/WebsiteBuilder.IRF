using Microsoft.AspNetCore.Mvc;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.ViewComponents
{
    public sealed class PageSectionViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(int sectionTypeId, string? settingsJson)
        {
            // Build a lightweight PageSection model for rendering
            var model = new PageSection
            {
                SectionTypeId = sectionTypeId,
                SettingsJson = settingsJson
            };

            // SectionTypes table:
            // 1 = Hero
            // 2 = Text
            var viewPath = sectionTypeId switch
            {
                1 => "~/Pages/Shared/Sections/_Hero.cshtml",
                2 => "~/Pages/Shared/Sections/_Text.cshtml",
                _ => "~/Pages/Shared/Sections/_Text.cshtml" // safe fallback
            };

            return View(viewPath, model);
        }
    }
}
