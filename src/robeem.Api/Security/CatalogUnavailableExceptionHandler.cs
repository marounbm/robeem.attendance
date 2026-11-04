using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using robeem.Application.Attendance.Catalog;
using robeem.Contracts.Tenancy;

namespace robeem.Api.Security;

/// <summary>
/// Answers 503 with the <c>catalog_unavailable</c> code when the prime tenant's Api could not give the catalog, so the caller can
/// tell an outage of the catalog from a fault of its own. Any other exception is left to the default handler.
/// </summary>
public sealed class CatalogUnavailableExceptionHandler : IExceptionHandler
{
    private readonly ILogger<CatalogUnavailableExceptionHandler> _logger;

    public CatalogUnavailableExceptionHandler(ILogger<CatalogUnavailableExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CatalogUnavailableException)
        {
            return false;
        }

        _logger.LogError(exception, "The attendance catalog of the prime tenant is unavailable.");

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "The attendance catalog is unavailable.",
            Detail = exception.Message,
        };
        problem.Extensions[TenancyProtocol.ErrorCodeExtension] = TenancyProtocol.CatalogUnavailableCode;

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
