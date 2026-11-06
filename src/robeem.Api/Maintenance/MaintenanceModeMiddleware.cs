using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using robeem.Api.Tenancy;
using robeem.Contracts.Tenancy;

namespace robeem.Api.Maintenance;

/// <summary>
/// While the databases are not ready, answers every controller action that works on tenant data with a 503 carrying the
/// <see cref="TenancyProtocol.DatabaseUnavailableCode"/> code, so the front end can say the application is in maintenance. An
/// action marked <see cref="AllowWithoutTenantAttribute"/> does not read tenant data and still answers.
/// </summary>
public sealed class MaintenanceModeMiddleware
{
    private readonly RequestDelegate _next;

    public MaintenanceModeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public static ProblemDetails CreateProblem(string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "The application is in maintenance mode.",
            Detail = detail,
        };
        problem.Extensions[TenancyProtocol.ErrorCodeExtension] = TenancyProtocol.DatabaseUnavailableCode;
        return problem;
    }

    public static Task WriteUnavailableAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsJsonAsync(CreateProblem(detail), context.RequestAborted);
    }

    public Task InvokeAsync(HttpContext context, DatabaseReadiness readiness)
    {
        if (readiness.IsReady || !UsesTenantData(context))
        {
            return _next(context);
        }

        return WriteUnavailableAsync(context, "The database is not reachable. The application is in maintenance mode.");
    }

    private static bool UsesTenantData(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null
            && endpoint.Metadata.GetMetadata<AllowWithoutTenantAttribute>() is null;
    }
}
