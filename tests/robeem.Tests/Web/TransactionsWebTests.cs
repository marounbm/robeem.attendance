using System.Net;
using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The raw and normalized punch screens: the client's requests, the controllers' clamping and the menu.</summary>
public sealed class TransactionsWebTests
{
    [Fact]
    public async Task TheClient_AsksForRawPunchesWithOnlyTheFiltersThatAreSet()
    {
        var all = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var some = new StubHttpHandler(HttpStatusCode.OK, """[{"id":4,"status":"Failed","processingError":"Unparseable timestamp 'x'","payload":"{}"}]""");

        await new TransactionsApiClient(all.CreateClient()).ListRawAsync(null, null, null, null, null, null, 100, CancellationToken.None);
        var rows = await new TransactionsApiClient(some.CreateClient()).ListRawAsync(3, 7, "Failed", new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), " 22 01 ", 50, CancellationToken.None);

        Assert.Equal("/api/attendance/transactions/raw?take=100", all.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(
            "/api/attendance/transactions/raw?take=50&integrationId=3&batchId=7&status=Failed&from=2026-04-01&to=2026-04-30&search=22%2001",
            some.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(("Failed", "Unparseable timestamp 'x'"), (rows[0].Status, rows[0].ProcessingError));
        Assert.Null(rows[0].NormalizedId);
    }

    [Fact]
    public async Task TheClient_AsksForNormalizedPunchesAndTheIntegrationOptions()
    {
        var normalized = new StubHttpHandler(HttpStatusCode.OK, """[{"id":2,"rawId":9,"eventType":"CheckIn","duplicateStatus":"Unique"}]""");
        var options = new StubHttpHandler(HttpStatusCode.OK, """[{"id":3,"name":"HQ"}]""");

        var rows = await new TransactionsApiClient(normalized.CreateClient())
            .ListNormalizedAsync(3, 9, "hiba", "CheckIn", "Unique", "None", new DateTime(2026, 4, 1), null, 20, CancellationToken.None);
        var integrations = await new TransactionsApiClient(options.CreateClient()).ListIntegrationsAsync(CancellationToken.None);

        Assert.Equal(
            "/api/attendance/transactions/normalized?take=20&integrationId=3&rawId=9&employee=hiba&eventType=CheckIn&duplicate=Unique&flag=None&from=2026-04-01",
            normalized.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal((2, 9, "CheckIn"), (rows[0].Id, rows[0].RawId, rows[0].EventType));
        Assert.Equal("/api/attendance/transactions/integrations", options.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("HQ", integrations.Single().Name);
    }

    [Fact]
    public async Task AForbiddenAnswer_IsNotSwallowed()
    {
        var client = new TransactionsApiClient(new StubHttpHandler(HttpStatusCode.Forbidden).CreateClient());

        await Assert.ThrowsAnyAsync<Exception>(() => client.ListRawAsync(null, null, null, null, null, null, 100, CancellationToken.None));
    }

    [Fact]
    public async Task TheRawScreen_ClampsRows_DropsAnUnknownStatus_AndKeepsTheFilterForTheForm()
    {
        var transactions = new FakeTransactions();
        var controller = new RawTransactionsController(transactions);

        var page = await controller.Index(3, 7, "Failed", null, null, "x", 100000, CancellationToken.None);
        var model = Assert.IsType<RawTransactionsIndexViewModel>(Assert.IsType<ViewResult>(page).Model);
        await controller.Index(null, null, "Bogus", null, null, null, 0, CancellationToken.None);

        Assert.Equal((3, 7, "Failed", "x", 500), (model.IntegrationId, model.BatchId, model.Status, model.Search, model.Take));
        Assert.Equal("HQ", Assert.Single(model.Integrations).Name);
        Assert.Equal([(500, "Failed"), (100, null)], transactions.RawAsks);
    }

    [Fact]
    public async Task TheNormalizedScreen_ClampsRows_AndDropsEveryUnknownName()
    {
        var transactions = new FakeTransactions();
        var controller = new NormalizedTransactionsController(transactions);

        var page = await controller.Index(null, 9, "hiba", "CheckIn", "Nope", "Nope", null, null, 100000, CancellationToken.None);
        var model = Assert.IsType<NormalizedTransactionsIndexViewModel>(Assert.IsType<ViewResult>(page).Model);
        await controller.Index(null, null, null, null, null, null, null, null, -1, CancellationToken.None);

        Assert.Equal((9, "hiba", "CheckIn", null, null, 500), (model.RawId, model.Employee, model.EventType, model.Duplicate, model.Flag, model.Take));
        Assert.Equal([500, 100], transactions.NormalizedTakes);
    }

    [Fact]
    public void BothScreens_AreInTheMenu_ForHrAndIntegrationAdministratorsOnly()
    {
        foreach (var controller in new[] { "RawTransactions", "NormalizedTransactions" })
        {
            var item = NavigationMenu.Find(controller);

            Assert.NotNull(item);
            Assert.Equal(2, item.Roles.Length);
            Assert.Contains(NavigationMenu.Integration, item.Roles);
            Assert.Contains(NavigationMenu.Hr, item.Roles);
            Assert.False(item.NeedsEmployee);
        }
    }

    private sealed class FakeTransactions : ITransactionsApiClient
    {
        public List<(int Take, string? Status)> RawAsks { get; } = [];

        public List<int> NormalizedTakes { get; } = [];

        public Task<IReadOnlyList<RawTransactionListItem>> ListRawAsync(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, int take, CancellationToken cancellationToken)
        {
            RawAsks.Add((take, status));
            return Task.FromResult<IReadOnlyList<RawTransactionListItem>>([]);
        }

        public Task<IReadOnlyList<NormalizedTransactionListItem>> ListNormalizedAsync(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int take, CancellationToken cancellationToken)
        {
            NormalizedTakes.Add(take);
            return Task.FromResult<IReadOnlyList<NormalizedTransactionListItem>>([]);
        }

        public Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransactionIntegrationOption>>([new TransactionIntegrationOption { Id = 3, Name = "HQ" }]);
    }
}
