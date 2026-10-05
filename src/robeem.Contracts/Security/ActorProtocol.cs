namespace robeem.Contracts.Security;

/// <summary>
/// The Development "act as" header (CLAUDE.md §2.8). The Web sends who the developer chose to act
/// as; robeem.Api honours it only through its development stand-in user, which the Api refuses to
/// start with outside Development. Real sign-in replaces it: a token carries the same facts as claims.
/// </summary>
public static class ActorProtocol
{
    public const string Header = "X-Dev-User";

    /// <summary>The roles of study §16.5, as the Api's <c>role</c> claim names them.</summary>
    public static readonly IReadOnlyList<string> Roles =
    [
        "Employee",
        "Manager",
        "HRAdministrator",
        "PayrollAdministrator",
        "IntegrationAdministrator",
    ];

    /// <summary>Builds the value: <c>{employee id, or -};{role,role}</c>.</summary>
    public static string Format(int? employeeId, IEnumerable<string> roles) =>
        $"{employeeId?.ToString(global::System.Globalization.CultureInfo.InvariantCulture) ?? "-"};{string.Join(",", roles)}";

    /// <summary>
    /// Reads a value. False for anything not exactly of the form, a non-positive employee id, or a
    /// role that is not one of <see cref="Roles"/>: a malformed header is refused, never half-read.
    /// </summary>
    public static bool TryParse(string? value, out int? employeeId, out IReadOnlyList<string> roles)
    {
        employeeId = null;
        roles = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(';');
        if (parts.Length != 2)
        {
            return false;
        }

        int? id = null;
        if (parts[0] != "-")
        {
            if (!int.TryParse(parts[0], global::System.Globalization.NumberStyles.None, global::System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                return false;
            }

            id = parsed;
        }

        var named = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (named.Any(r => !Roles.Contains(r, StringComparer.Ordinal)))
        {
            return false;
        }

        employeeId = id;
        roles = named.Distinct(StringComparer.Ordinal).ToList();
        return true;
    }
}
