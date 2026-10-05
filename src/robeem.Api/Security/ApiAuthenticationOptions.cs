namespace robeem.Api.Security;

/// <summary>Authentication settings of the Api (CLAUDE.md §2.8). Section <see cref="SectionName"/>.</summary>
public sealed class ApiAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public JwtSettings Jwt { get; set; } = new();

    public DevelopmentUserSettings DevelopmentUser { get; set; } = new();

    /// <summary>The identity provider whose tokens are accepted.</summary>
    public sealed class JwtSettings
    {
        /// <summary>Gets or sets the issuer's address, e.g. <c>https://login.microsoftonline.com/{tenant}/v2.0</c>. Its OpenID Connect metadata gives the signing keys.</summary>
        public string? Authority { get; set; }

        /// <summary>Gets or sets the audience the tokens are issued for: this Api's application id or URI.</summary>
        public string? Audience { get; set; }
    }

    /// <summary>A stand-in user for local work with the Web, which has no sign-in yet. Refused outside Development.</summary>
    public sealed class DevelopmentUserSettings
    {
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the <c>Profile</c> id of the stand-in user; none by default.</summary>
        public int? EmployeeId { get; set; }

        /// <summary>Gets or sets the roles of the stand-in user. Every role by default.</summary>
        public string[] Roles { get; set; } = [.. AttendanceRoles.All];
    }
}
