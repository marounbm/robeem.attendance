using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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
            case ApiAccessDeniedException denied:
                context.Result = AccessDeniedPage.Create(context.ModelState, denied.Reason is null ? null : $"robeem.Api refused this: {denied.Reason}");
                context.ExceptionHandled = true;
                break;

            case ApiNotFoundException:
                context.Result = new NotFoundResult();
                context.ExceptionHandled = true;
                break;
        }
    }
}
