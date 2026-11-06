using Microsoft.AspNetCore.Diagnostics;
using robeem.Infrastructure.Persistence;

namespace robeem.Api.Maintenance;

/// <summary>
/// Answers 503 with the <c>database_unavailable</c> code when a request failed because the database went away after the Api
/// started, so the front end shows the maintenance message instead of a server error. Any other exception is left to the default handler.
/// </summary>
public sealed class DatabaseUnavailableExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DatabaseUnavailableExceptionHandler> _logger;

    public DatabaseUnavailableExceptionHandler(ILogger<DatabaseUnavailableExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!DatabaseConnectivity.IsUnreachable(exception))
        {
            return false;
        }

        _logger.LogError(exception, "The database is not reachable.");
        await MaintenanceModeMiddleware.WriteUnavailableAsync(httpContext, "The database is not reachable. The application is in maintenance mode.");
        return true;
    }
}
