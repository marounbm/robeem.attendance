using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using robeem.Web.ApiClients;
using robeem.Web.Tenancy;

namespace robeem.Tests.Web;

public sealed class DatabaseUnavailableTests
{
    private const string MaintenanceProblem = """{"title":"The application is in maintenance mode.","status":503,"code":"database_unavailable"}""";

    [Fact]
    public async Task CheckDatabase_ThrowsWhenTheApiReportsMaintenance()
    {
        var handler = new StubHttpHandler(HttpStatusCode.ServiceUnavailable, MaintenanceProblem, "application/problem+json");
        var client = new TenantsApiClient(handler.CreateClient());

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => client.CheckDatabaseAsync(CancellationToken.None));

        Assert.Equal("/api/tenants/current/database", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CheckDatabase_IsQuietWhenTheDatabaseAnswers()
    {
        var client = new TenantsApiClient(new StubHttpHandler(HttpStatusCode.NoContent).CreateClient());

        await client.CheckDatabaseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AnyScreensCall_ThrowsWhenTheApiReportsMaintenance()
    {
        var client = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.ServiceUnavailable, MaintenanceProblem, "application/problem+json").CreateClient());

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => client.GetAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task A503ThatIsNotMaintenance_IsAnOrdinaryFailure()
    {
        var client = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.ServiceUnavailable, """{"code":"catalog_unavailable"}""", "application/problem+json").CreateClient());

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(1, CancellationToken.None));

        Assert.IsNotType<DatabaseUnavailableException>(failure);
    }

    [Fact]
    public void TheFilter_ShowsTheMaintenancePageWith503()
    {
        var context = ExceptionContextFor(new DatabaseUnavailableException());

        new DatabaseUnavailableFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        var view = Assert.IsType<ViewResult>(context.Result);
        Assert.Equal("Maintenance", view.ViewName);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, view.StatusCode);
    }

    [Fact]
    public void TheFilter_LeavesOtherExceptionsAlone()
    {
        var context = ExceptionContextFor(new HttpRequestException("robeem.Api is down."));

        new DatabaseUnavailableFilter().OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    private static ExceptionContext ExceptionContextFor(Exception exception)
        => new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = exception,
        };
}
