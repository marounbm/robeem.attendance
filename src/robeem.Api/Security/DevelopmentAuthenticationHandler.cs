using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using robeem.Contracts.Security;

namespace robeem.Api.Security;

/// <summary>
/// Authenticates every request as the configured stand-in user, so the Web (which has no sign-in
/// yet) works locally. Registered only when <c>Authentication:DevelopmentUser:Enabled</c> is on,
/// which the Api refuses outside Development (<see cref="SecurityServiceCollectionExtensions"/>).
/// It carries no <c>tenant_id</c>: in Development the tenant is picked by header, as before. A
/// request with the <see cref="ActorProtocol.Header"/> header acts as the employee and roles it
/// names (the Web's "act as" switcher) instead of the configured user.
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
        // Who the developer chose to act as (the Web's "act as" switcher), else the configured stand-in.
        var user = _options.DevelopmentUser;
        var employeeId = user.EmployeeId;
        IEnumerable<string> roles = user.Roles;
        if (Request.Headers.TryGetValue(ActorProtocol.Header, out var header))
        {
            // A malformed header is refused, not ignored: acting as the wrong person silently would mislead.
            if (!ActorProtocol.TryParse(header.ToString(), out var actingId, out var actingRoles))
            {
                return Task.FromResult(AuthenticateResult.Fail($"The {ActorProtocol.Header} header is malformed."));
            }

            (employeeId, roles) = (actingId, actingRoles);
        }

        var claims = new List<Claim> { new("sub", "development-user") };
        claims.AddRange(roles.Select(role => new Claim(SecurityClaims.Role, role)));
        if (employeeId is { } id)
        {
            claims.Add(new Claim(SecurityClaims.EmployeeId, id.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", SecurityClaims.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
