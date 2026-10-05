using System.Net;

namespace robeem.Web.ApiClients;

/// <summary>
/// robeem.Api has no such record (404) where the screen expected one. Handled by
/// <see cref="Actors.ApiAccessDeniedFilter"/> as a not-found page.
/// </summary>
public sealed class ApiNotFoundException : HttpRequestException
{
    public ApiNotFoundException()
        : base("robeem.Api returned 404 for a record the screen expected.", null, HttpStatusCode.NotFound)
    {
    }
}
