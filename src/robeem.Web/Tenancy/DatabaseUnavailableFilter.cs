using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Web.ApiClients;

namespace robeem.Web.Tenancy;

/// <summary>
/// When robeem.Api reports that its database cannot be reached, shows the maintenance page (a 503) in place of the screen.
/// The rest of the application keeps running, so the page comes back to normal on its own once the database does.
/// </summary>
public sealed class DatabaseUnavailableFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DatabaseUnavailableException)
        {
            return;
        }

        context.Result = new ViewResult
        {
            ViewName = "Maintenance",
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), context.ModelState),
        };
        context.ExceptionHandled = true;
    }
}
