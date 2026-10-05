using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Web.ApiClients;

namespace robeem.Web.Actors;

/// <summary>
/// Turns robeem.Api's refusals into pages: a 401/403 shows <c>AccessDenied</c> (what the acting
/// role may not do, and how to switch), a 404 shows the not-found page.
/// </summary>
public sealed class ApiAccessDeniedFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ApiAccessDeniedException:
                context.Result = new ViewResult
                {
                    ViewName = "AccessDenied",
                    StatusCode = StatusCodes.Status403Forbidden,
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), context.ModelState),
                };
                context.ExceptionHandled = true;
                break;

            case ApiNotFoundException:
                context.Result = new NotFoundResult();
                context.ExceptionHandled = true;
                break;
        }
    }
}
