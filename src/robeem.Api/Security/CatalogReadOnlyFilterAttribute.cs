using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using robeem.Application.Attendance.Catalog;
using robeem.Contracts.Tenancy;

namespace robeem.Api.Security;

/// <summary>
/// Answers a save of the platform catalog from a tenant other than the prime tenant with 403 and the <c>catalog_read_only</c>
/// code, so the front end can say so, instead of a server error. The role check comes first; this is the second line.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CatalogReadOnlyFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not CatalogReadOnlyException)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "The attendance catalog is read-only here.",
            Detail = context.Exception.Message,
        };
        problem.Extensions[TenancyProtocol.ErrorCodeExtension] = TenancyProtocol.CatalogReadOnlyCode;

        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
        context.ExceptionHandled = true;
    }
}
