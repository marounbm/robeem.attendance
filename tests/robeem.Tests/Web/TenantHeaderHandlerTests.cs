using System.Net;
using Microsoft.AspNetCore.Http;
using robeem.Contracts.Tenancy;
using robeem.Web.Tenancy;

namespace robeem.Tests.Web;

public sealed class TenantHeaderHandlerTests
{
    [Theory]
    [InlineData("acme", "acme")]
    [InlineData("not a tenant\r\nX-Evil: 1", null)]
    [InlineData(null, null)]
    public async Task ForwardsOnlyWellFormedTenantCookie(string? cookie, string? expectedHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = $"{TenantCookie.Name}={Uri.EscapeDataString(cookie)}";
        }

        var stub = new StubHttpHandler(HttpStatusCode.OK);
        var handler = new TenantHeaderHandler(new HttpContextAccessor { HttpContext = httpContext }) { InnerHandler = stub };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };

        await client.GetAsync("api/tenants/current");

        var request = stub.Requests.Single();
        var sent = request.Headers.TryGetValues(TenancyProtocol.TenantHeader, out var values) ? values.Single() : null;
        Assert.Equal(expectedHeader, sent);
    }
}
