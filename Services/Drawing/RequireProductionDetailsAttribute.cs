using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KarakusAluminyum.Web.Services.Drawing;

// Üretim detayları ayarlardan kapalıyken imalat sayfaları hiç yokmuş gibi davranır.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireProductionDetailsAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var settings = await context.HttpContext.RequestServices.GetRequiredService<SiteSettingsProvider>().GetAsync();
        if (settings.ProductionDetails) await next();
        else context.Result = new NotFoundResult();
    }
}
