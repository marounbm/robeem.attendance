using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using robeem.Application.Security;

namespace robeem.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    private const string SelectorScheme = "robeem";

    /// <summary>
    /// Adds JWT bearer authentication against the configured identity provider, role-based
    /// authorization, and <see cref="ICurrentUser"/>. Fails at startup, not at the first request, when
    /// outside Development the provider is not configured or the stand-in user is on (CLAUDE.md §2.8).
    /// </summary>
    public static IServiceCollection AddRobeemAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(ApiAuthenticationOptions.SectionName).Get<ApiAuthenticationOptions>() ?? new ApiAuthenticationOptions();
        services.Configure<ApiAuthenticationOptions>(configuration.GetSection(ApiAuthenticationOptions.SectionName));

        var useDevelopmentUser = options.DevelopmentUser.Enabled;
        if (!environment.IsDevelopment())
        {
            if (useDevelopmentUser)
            {
                throw new InvalidOperationException($"{ApiAuthenticationOptions.SectionName}:DevelopmentUser:Enabled authenticates everyone as a user with every role, so it is only allowed in Development.");
            }

            if (string.IsNullOrWhiteSpace(options.Jwt.Authority) || string.IsNullOrWhiteSpace(options.Jwt.Audience))
            {
                throw new InvalidOperationException($"{ApiAuthenticationOptions.SectionName}:Jwt:Authority and :Audience must be set: the Api accepts no tokens without them.");
            }
        }

        var authentication = services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, SelectorScheme, scheme =>
            {
                // A bearer token is always validated; the stand-in user only answers requests without one.
                scheme.ForwardDefaultSelector = context =>
                    useDevelopmentUser && !context.Request.Headers.Authorization.Any()
                        ? DevelopmentAuthenticationHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, bearer =>
            {
                bearer.Authority = options.Jwt.Authority;
                bearer.Audience = options.Jwt.Audience;
                bearer.RequireHttpsMetadata = environment.IsProduction();

                // Claims keep the names the token gave them: `role`, `tenant_id`, `employee_id`.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "sub",
                    RoleClaimType = SecurityClaims.Role,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        if (useDevelopmentUser)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, null);
        }

        services.AddAuthorization();
        services.AddHttpContextAccessor();

        // After AddRobeemApplication, which registers the system user: this one wins.
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}
