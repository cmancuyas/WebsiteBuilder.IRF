using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace WebsiteBuilder.IRF.Infrastructure.Razor;

public interface IRazorPartialRenderer
{
    Task<string> RenderPartialAsync(string partialName, object model, HttpContext httpContext);
}

public sealed class RazorPartialRenderer : IRazorPartialRenderer
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    public RazorPartialRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> RenderPartialAsync(string partialName, object model, HttpContext httpContext)
    {
        var actionContext = new ActionContext(
            httpContext,
            httpContext.GetRouteData() ?? new RouteData(),
            new ActionDescriptor()
        );

        var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath: partialName, isMainPage: false);
        if (!viewResult.Success)
        {
            viewResult = _viewEngine.FindView(actionContext, partialName, isMainPage: false);
        }

        if (!viewResult.Success || viewResult.View is null)
        {
            var searched = string.Join(Environment.NewLine, viewResult.SearchedLocations ?? Enumerable.Empty<string>());
            throw new InvalidOperationException($"Partial view '{partialName}' not found. Searched:{Environment.NewLine}{searched}");
        }

        await using var sw = new StringWriter();

        var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        var tempData = new TempDataDictionary(httpContext, _tempDataProvider);

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            tempData,
            sw,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }
}
