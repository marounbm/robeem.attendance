using System.Net;

namespace robeem.Web.ApiClients;

/// <summary>
/// robeem.Api could not reach its database (503, <c>database_unavailable</c>): the application is in maintenance mode.
/// Handled by <see cref="Tenancy.DatabaseUnavailableFilter"/> as the maintenance page. It is an <see cref="HttpRequestException"/>
/// so the layout's parts, which already tolerate an Api that does not answer, tolerate it too.
/// </summary>
public sealed class DatabaseUnavailableException : HttpRequestException
{
    public DatabaseUnavailableException()
        : base("robeem.Api could not reach its database.", null, HttpStatusCode.ServiceUnavailable)
    {
    }
}
