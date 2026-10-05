using robeem.Contracts.Security;

namespace robeem.Web.Actors;

/// <summary>
/// The cookies holding the Development "act as" choice (CLAUDE.md §2.8). The value of
/// <see cref="Name"/> is exactly what <see cref="ActorProtocol"/> sends to robeem.Api, and is
/// re-validated on every read: a malformed or tampered cookie is never forwarded.
/// </summary>
public static class ActorCookie
{
    public const string Name = "robeem.actor";

    public const string DisplayNameCookie = "robeem.actor.name";

    private const int DisplayNameMaxLength = 120;

    /// <summary>Gets the choice, or <c>null</c> when there is none or the cookie is malformed.</summary>
    public static ActingAs? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(Name, out var value) || !ActorProtocol.TryParse(value, out var employeeId, out var roles))
        {
            return null;
        }

        var name = request.Cookies.TryGetValue(DisplayNameCookie, out var display) && !string.IsNullOrWhiteSpace(display)
            ? Clean(display)
            : employeeId is { } id ? $"Employee {id}" : "No employee";
        return new ActingAs(employeeId, name, roles);
    }

    /// <summary>Gets the header value to forward to robeem.Api, or <c>null</c> when there is no valid choice.</summary>
    public static string? ReadHeaderValue(HttpRequest request)
        => Read(request) is { } acting ? ActorProtocol.Format(acting.EmployeeId, acting.Roles) : null;

    public static void Write(HttpResponse response, bool secure, int? employeeId, string name, IEnumerable<string> roles)
    {
        var options = new CookieOptions { HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, Secure = secure };
        response.Cookies.Append(Name, ActorProtocol.Format(employeeId, roles), options);
        response.Cookies.Append(DisplayNameCookie, Clean(name), options);
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Name);
        response.Cookies.Delete(DisplayNameCookie);
    }

    // The name is only shown, never trusted; strip control characters and cap the length.
    private static string Clean(string name)
    {
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length <= DisplayNameMaxLength ? cleaned : cleaned[..DisplayNameMaxLength];
    }
}
