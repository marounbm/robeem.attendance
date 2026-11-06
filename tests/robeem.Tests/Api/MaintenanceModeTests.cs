using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Npgsql;
using robeem.Api.Maintenance;
using robeem.Api.Tenancy;
using robeem.Contracts.Tenancy;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Api;

public sealed class MaintenanceModeTests
{
    [Fact]
    public void ADatabaseThatCannotBeReached_IsAnOutage_WhereverItIsWrapped()
    {
        var refused = new NpgsqlException("Failed to connect to 127.0.0.1:5432", new IOException("Connection refused"));

        Assert.True(DatabaseConnectivity.IsUnreachable(refused));
        Assert.True(DatabaseConnectivity.IsUnreachable(new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", refused)));
        Assert.True(DatabaseConnectivity.IsUnreachable(new AggregateException(refused)));
    }

    [Theory]
    [InlineData("08006")]
    [InlineData("28P01")]
    [InlineData("53300")]
    [InlineData("57P03")]
    public void AServerThatRefusesTheConnection_IsAnOutage(string sqlState)
    {
        Assert.True(DatabaseConnectivity.IsUnreachable(new PostgresException("refused", "FATAL", "FATAL", sqlState)));
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("42P01")]
    public void AStatementThatFails_IsNotAnOutage(string sqlState)
    {
        Assert.False(DatabaseConnectivity.IsUnreachable(new PostgresException("failed", "ERROR", "ERROR", sqlState)));
    }

    [Fact]
    public void AnIoErrorOrABug_IsNotAnOutage()
    {
        Assert.False(DatabaseConnectivity.IsUnreachable(new IOException("The file is in use.")));
        Assert.False(DatabaseConnectivity.IsUnreachable(new InvalidOperationException("Sequence contains no elements.")));
    }

    [Fact]
    public async Task WhileTheDatabasesAreNotReady_AnActionOnTenantData_Answers503WithTheMaintenanceCode()
    {
        var nextCalled = false;
        var middleware = new MaintenanceModeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ContextFor(allowWithoutTenant: false);

        await middleware.InvokeAsync(context, new DatabaseReadiness());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(TenancyProtocol.DatabaseUnavailableCode, body.RootElement.GetProperty(TenancyProtocol.ErrorCodeExtension).GetString());
    }

    [Fact]
    public async Task WhileTheDatabasesAreNotReady_AnActionThatNeedsNoTenant_StillAnswers()
    {
        var nextCalled = false;
        var middleware = new MaintenanceModeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ContextFor(allowWithoutTenant: true), new DatabaseReadiness());

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task OnceTheDatabasesAreReady_RequestsPassThrough()
    {
        var nextCalled = false;
        var middleware = new MaintenanceModeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ContextFor(allowWithoutTenant: false), new DatabaseReadiness(ready: true));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Readiness_ReleasesWhoeverWaits_WhenItIsMarkedReady()
    {
        var readiness = new DatabaseReadiness();
        var waiting = readiness.WaitUntilReadyAsync(CancellationToken.None);

        Assert.False(readiness.IsReady);
        Assert.False(waiting.IsCompleted);

        readiness.MarkReady();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(readiness.IsReady);
    }

    private static DefaultHttpContext ContextFor(bool allowWithoutTenant)
    {
        var metadata = new List<object> { new ControllerActionDescriptor() };
        if (allowWithoutTenant)
        {
            metadata.Add(new AllowWithoutTenantAttribute());
        }

        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));
        context.Response.Body = new MemoryStream();
        return context;
    }
}
