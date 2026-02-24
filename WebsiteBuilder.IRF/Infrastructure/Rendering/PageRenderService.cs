using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.Infrastructure.Rendering;

public sealed class PageRenderService : IPageRenderService
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _services;

    // ✅ Your folder structure
    private const string SectionsRoot = "~/Pages/Shared/Sections/";
    private const string UnknownPartial = SectionsRoot + "_Unknown.cshtml";

    public PageRenderService(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider services)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _services = services;
    }

    public async Task<string> RenderRevisionSectionsAsync(PageRevision revision, CancellationToken ct = default)
    {
        if (revision is null) throw new ArgumentNullException(nameof(revision));
        if (revision.Sections is null || revision.Sections.Count == 0) return "";

        var sb = new StringBuilder(4096);

        foreach (var s in revision.Sections
                     .Where(x => !x.IsDeleted && x.IsActive)
                     .OrderBy(x => x.SortOrder))
        {
            ct.ThrowIfCancellationRequested();

            var key = s.SectionType?.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                // No key => render unknown
                sb.AppendLine(await RenderUnknownAsync(
                    reason: "Missing SectionType.Key.",
                    missingPartialPath: null,
                    settingsJson: s.SettingsJson,
                    ct: ct));
                continue;
            }

            var partialStem = NormalizeKeyToPascalCase(key);
            var viewPath = $"{SectionsRoot}_{partialStem}.cshtml";

            // ✅ Your partials expect `@model string?` (SettingsJson)
            var html = await RenderPartialOrNullAsync(viewPath, (string?)s.SettingsJson);

            if (html is null)
            {
                html = await RenderUnknownAsync(
                    reason: null,
                    missingPartialPath: viewPath,
                    settingsJson: s.SettingsJson,
                    ct: ct);
            }

            sb.AppendLine(html);
        }

        return sb.ToString();
    }

    private static string NormalizeKeyToPascalCase(string key)
    {
        // "hero" => "Hero"
        // "hero-banner" => "HeroBanner"
        // "text_block" => "TextBlock"
        var parts = key.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "Unknown";

        var ti = CultureInfo.InvariantCulture.TextInfo;
        return string.Concat(parts.Select(p => ti.ToTitleCase(p.ToLowerInvariant())));
    }

    private async Task<string?> RenderPartialOrNullAsync(string viewPath, object? model)
    {
        var httpContext = new DefaultHttpContext { RequestServices = _services };
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new ActionDescriptor());

        var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        if (!viewResult.Success)
            return null;

        await using var sw = new StringWriter();

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        var tempData = new TempDataDictionary(actionContext.HttpContext, _tempDataProvider);

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            sw,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }

    private async Task<string> RenderUnknownAsync(
        string? reason,
        string? missingPartialPath,
        string? settingsJson,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // ✅ Expect _Unknown.cshtml to exist and accept a simple object model
        var unknownModel = new UnknownSectionModel(
            MissingPartialPath: missingPartialPath,
            Reason: reason,
            SettingsJson: settingsJson
        );

        var html = await RenderPartialOrNullAsync(UnknownPartial, unknownModel);
        return html ?? RenderInlineMissing(missingPartialPath ?? reason ?? "Unknown section.");
    }

    private static string RenderInlineMissing(string message)
    {
        return $"""
                <div class="alert alert-warning border mb-3">
                    <div class="fw-semibold">Section render skipped</div>
                    <div class="small text-muted">{System.Net.WebUtility.HtmlEncode(message)}</div>
                </div>
                """;
    }

    public sealed record UnknownSectionModel(
        string? MissingPartialPath,
        string? Reason,
        string? SettingsJson
    );
}
