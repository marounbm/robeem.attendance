using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;

namespace robeem.Web.Tenancy;

/// <summary>
/// When robeem.Api cannot resolve the request's tenant, sends a GET to the tenant picker (and
/// forgets a picked tenant the API no longer knows); any other method gets a 403.
/// </summary>
public sealed class TenantSelectionRequiredFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not TenantSelectionRequiredException exception)
        {
            return;
        }

        var http = context.HttpContext;
        if (exception.IsUnknownTenant)
        {
            http.Response.Cookies.Delete(TenantCookie.Name);
        }

        if (HttpMethods.IsGet(http.Request.Method))
        {
            var returnUrl = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            context.Result = new RedirectToActionResult(nameof(TenantsController.Index), "Tenants", new { returnUrl });
        }
        else
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                Content = "No tenant could be resolved for this request.",
            };
        }

        context.ExceptionHandled = true;
    }
}
