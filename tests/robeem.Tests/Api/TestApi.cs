using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using robeem.Api.Controllers;
using robeem.Api.Security;
using robeem.Application.Security;

namespace robeem.Tests.Api;

/// <summary>
/// The Api's controllers on an in-memory server, with the real routing, authorization and
/// <see cref="HttpCurrentUser"/>, and whatever services a test registers. The signed-in user is set
/// by a header, standing in for the claims of a validated token (the token itself is tested in
/// <c>ApiAuthenticationTests</c>).
/// </summary>
public sealed class TestApi : IAsyncDisposable
{
    private const string UserHeader = "X-Test-User";

    private readonly IHost _host;

    private TestApi(IHost host)
    {
        _host = host;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    /// <summary>Starts the server; <paramref name="configure"/> registers the application services the controllers need.</summary>
    public static TestApi Start(Action<IServiceCollection> configure)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(AttendanceCorrectionsController).Assembly);
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", null);
                    services.AddAuthorization();
                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentUser, HttpCurrentUser>();
                    configure(services);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .Build();
        host.Start();
        return new TestApi(host);
    }

    /// <summary>A user for <see cref="Send"/>: their employee id (or none) and roles.</summary>
    public static string As(int? employeeId, params string[] roles) =>
        $"{employeeId?.ToString() ?? "-"};{string.Join(",", roles)}";

    public async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> SendFile(string url, string? user, string fileName, byte[] content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", fileName } };
        request.Content = form;
        return await Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>Signs in whoever the header names: <c>{employee id or -};{roles, comma-separated}</c>.</summary>
    private sealed class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var value))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = value.ToString().Split(';');
            var claims = new List<Claim> { new("sub", "test") };
            claims.AddRange(parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role => new Claim(SecurityClaims.Role, role)));
            if (parts[0] != "-")
            {
                claims.Add(new Claim(SecurityClaims.EmployeeId, parts[0]));
            }

            var identity = new ClaimsIdentity(claims, "Test", "sub", SecurityClaims.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
