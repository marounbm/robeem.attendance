namespace robeem.Api.Security;

/// <summary>The claims the identity provider's token must carry (CLAUDE.md §2.8).</summary>
public static class SecurityClaims
{
    /// <summary>The tenant database; read by <c>TenantResolutionMiddleware</c>.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>One claim per role of <see cref="AttendanceRoles"/>.</summary>
    public const string Role = "role";

    /// <summary>The user's <c>Profile</c> id.</summary>
    public const string EmployeeId = "employee_id";
}
