using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace robeem.Api.Security;

/// <summary>
/// Authenticates every request as the configured stand-in user, so the Web (which has no sign-in
/// yet) works locally. Registered only when <c>Authentication:DevelopmentUser:Enabled</c> is on,
/// which the Api refuses outside Development (<see cref="SecurityServiceCollectionExtensions"/>).
/// It carries no <c>tenant_id</c>: in Development the tenant is picked by header, as before.
/// </summary>
public sealed class DevelopmentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Development";

    private readonly ApiAuthenticationOptions _options;

    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApiAuthenticationOptions> options)
        : base(schemeOptions, logger, encoder)
    {
        _options = options.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = _options.DevelopmentUser;
        var claims = new List<Claim> { new("sub", "development-user") };
        claims.AddRange(user.Roles.Select(role => new Claim(SecurityClaims.Role, role)));
        if (user.EmployeeId is { } employeeId)
        {
            claims.Add(new Claim(SecurityClaims.EmployeeId, employeeId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", SecurityClaims.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
