using System.Text.RegularExpressions;

namespace robeem.Web.Tenancy;

/// <summary>The cookie holding the tenant picked in the development tenant picker.</summary>
public static class TenantCookie
{
    public const string Name = "robeem.tenant";

    // Same shape as a configured tenant id (lower-case letters, digits, '-', max 64), compared
    // case-insensitively like the API's registry. Anything else is never forwarded as a header.
    private static readonly Regex TenantIdPattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Gets the picked tenant id, or <c>null</c> when there is none or it is malformed.</summary>
    public static string? Read(HttpRequest request)
        => request.Cookies.TryGetValue(Name, out var value) && value is not null && TenantIdPattern.IsMatch(value) ? value : null;
}
