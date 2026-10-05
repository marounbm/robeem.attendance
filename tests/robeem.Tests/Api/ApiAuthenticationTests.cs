using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using robeem.Api.Controllers;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Tests.Api;

/// <summary>
/// The Api's authentication (P12, CLAUDE.md §2.8): a real signed JWT through the real
/// <c>AddRobeemAuthentication</c> setup, with the identity provider's metadata replaced by a local key.
/// </summary>
public sealed class ApiAuthenticationTests
{
    private const string Issuer = "https://idp.test/";
    private const string Audience = "robeem-api";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("a-test-signing-key-of-at-least-32-bytes!"));

    [Fact]
    public async Task AValidToken_SignsTheUserIn_WithTheClaimsAsTheTokenNamedThem()
    {
        using var host = await StartAsync(developmentUser: false);

        var response = await host.Client.SendAsync(Request("whoami", Token(roles: [AttendanceRoles.Manager], employeeId: 12, tenantId: "acme")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var who = await response.Content.ReadFromJsonAsync<WhoAmI>();
        Assert.Equal("acme", who!.TenantId);
        Assert.Equal("12", who.EmployeeId);
        Assert.True(who.IsManager);
        Assert.False(who.IsHr);
        Assert.Equal("employee:12", who.Actor);
    }

    [Fact]
    public async Task ARoleInTheTokenDecidesWhatTheUserMayCall()
    {
        using var host = await StartAsync(developmentUser: false);

        var manager = await host.Client.SendAsync(Request("api/attendance/corrections/5/decision", Token([AttendanceRoles.Manager], 12), new { approved = true }));
        var employee = await host.Client.SendAsync(Request("api/attendance/corrections/5/decision", Token([AttendanceRoles.Employee], 7), new { approved = true }));

        Assert.Equal(HttpStatusCode.OK, manager.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
    }

    [Theory]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-key")]
    [InlineData("expired")]
    public async Task ATokenThatIsNotValid_IsRefused(string flaw)
    {
        using var host = await StartAsync(developmentUser: false);

        var response = await host.Client.SendAsync(Request("whoami", Token([AttendanceRoles.Manager], 12, flaw: flaw)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithoutAToken_NobodyIsSignedIn()
    {
        using var host = await StartAsync(developmentUser: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(Request("whoami", null))).StatusCode);
    }

    [Fact]
    public async Task InDevelopment_TheStandInUser_AnswersRequestsWithoutAToken_WithEveryRole()
    {
        using var host = await StartAsync(developmentUser: true);

        var response = await host.Client.SendAsync(Request("whoami", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var who = await response.Content.ReadFromJsonAsync<WhoAmI>();
        Assert.True(who!.IsManager && who.IsHr);
        Assert.Null(who.TenantId);
    }

    [Fact]
    public async Task TheStandInUser_NeverReplacesABadToken()
    {
        using var host = await StartAsync(developmentUser: true);

        var response = await host.Client.SendAsync(Request("whoami", Token([AttendanceRoles.Manager], 12, flaw: "wrong-key")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void TheStandInUser_IsRefusedOutsideDevelopment()
    {
        var configuration = Configuration(developmentUser: true);

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRobeemAuthentication(configuration, new FakeEnvironment(Environments.Production)));

        Assert.Contains("DevelopmentUser", error.Message);
    }

    [Theory]
    [InlineData("", "robeem-api")]
    [InlineData("https://idp.test/", "")]
    public void OutsideDevelopment_TheIdentityProviderMustBeConfigured(string authority, string audience)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Jwt:Authority"] = authority, ["Authentication:Jwt:Audience"] = audience })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddRobeemAuthentication(configuration, new FakeEnvironment(Environments.Production)));

        Assert.Contains("Authority", error.Message);
    }

    [Fact]
    public void InDevelopment_NothingNeedsToBeConfigured()
    {
        new ServiceCollection().AddRobeemAuthentication(new ConfigurationBuilder().Build(), new FakeEnvironment(Environments.Development));
    }

    private static IConfiguration Configuration(bool developmentUser) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Jwt:Authority"] = Issuer,
            ["Authentication:Jwt:Audience"] = Audience,
            ["Authentication:DevelopmentUser:Enabled"] = developmentUser.ToString(),
        })
        .Build();

    private static HttpRequestMessage Request(string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static string Token(string[] roles, int? employeeId = null, string? tenantId = null, string? flaw = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "user-1", [SecurityClaims.Role] = roles };
        if (employeeId is { } id)
        {
            claims[SecurityClaims.EmployeeId] = id.ToString();
        }

        if (tenantId is not null)
        {
            claims[SecurityClaims.TenantId] = tenantId;
        }

        var key = flaw == "wrong-key" ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-test-signing-key-32-bytes-long!!")) : SigningKey;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = flaw == "wrong-issuer" ? "https://someone-else.test/" : Issuer,
            Audience = flaw == "wrong-audience" ? "another-api" : Audience,
            Claims = claims,
            NotBefore = flaw == "expired" ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow.AddMinutes(-1),
            Expires = flaw == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
    }

    private static async Task<TestApi> StartAsync(bool developmentUser)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(AttendanceCorrectionsController).Assembly);
                    services.AddSingleton<IAttendanceCorrectionService, OkCorrectionService>();
                    services.AddRobeemAuthentication(Configuration(developmentUser), new FakeEnvironment(Environments.Development));

                    // The provider's metadata is a local key: no network. Runs before the post-configuration that would fetch it.
                    services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                        configuration.SigningKeys.Add(SigningKey);
                        options.Configuration = configuration;
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapGet("/whoami", (HttpContext context, robeem.Application.Security.ICurrentUser user) =>
                            context.User.Identity?.IsAuthenticated == true
                                ? Results.Ok(new WhoAmI(
                                    context.User.FindFirst(SecurityClaims.TenantId)?.Value,
                                    context.User.FindFirst(SecurityClaims.EmployeeId)?.Value,
                                    context.User.IsInRole(AttendanceRoles.Manager),
                                    context.User.IsInRole(AttendanceRoles.HRAdministrator),
                                    user.Actor))
                                : Results.Unauthorized());
                    });
                }))
            .Build();
        await host.StartAsync();
        return new TestApi(host);
    }

    private sealed record WhoAmI(string? TenantId, string? EmployeeId, bool IsManager, bool IsHr, string Actor);

    private sealed class TestApi : IDisposable
    {
        private readonly IHost _host;

        public TestApi(IHost host)
        {
            _host = host;
            Client = host.GetTestClient();
        }

        public HttpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _host.Dispose();
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "robeem.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class OkCorrectionService : IAttendanceCorrectionService
    {
        public Task<CorrectionResult> ProposeCorrectionAsync(int exceptionId, DateTime proposedValue, string justification, int requestedByEmployeeId, CancellationToken cancellationToken) =>
            Task.FromResult(CorrectionResult.Success(new robeem.DataModel.AttendanceCorrection { Id = 1 }, robeem.DataModel.AttendanceExceptionStatus.PendingCorrection));

        public Task<CorrectionResult> DecideCorrectionAsync(int correctionId, int decidedByUserId, bool approved, bool isHrDecision, CancellationToken cancellationToken) =>
            Task.FromResult(CorrectionResult.Success(new robeem.DataModel.AttendanceCorrection { Id = correctionId, Status = robeem.DataModel.AttendanceCorrectionStatus.Applied }, robeem.DataModel.AttendanceExceptionStatus.Closed));

        public Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover approver, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CorrectionListItem>>([]);
    }
}
