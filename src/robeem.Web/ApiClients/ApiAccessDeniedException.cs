using System.Net;

namespace robeem.Web.ApiClients;

/// <summary>
/// robeem.Api refused the request for lack of a role (403) or a sign-in (401). Handled by
/// <see cref="Actors.ApiAccessDeniedFilter"/>, which shows a page that says what to do.
/// </summary>
public sealed class ApiAccessDeniedException : HttpRequestException
{
    public ApiAccessDeniedException(HttpStatusCode statusCode, string? reason = null)
        : base($"robeem.Api refused the request ({(int)statusCode}){(reason is null ? string.Empty : ": " + reason.TrimEnd('.'))}.", null, statusCode)
    {
        Reason = reason;
    }

    /// <summary>Gets the Api's own explanation (its problem title), when it gave one beyond the status text.</summary>
    public string? Reason { get; }
}
