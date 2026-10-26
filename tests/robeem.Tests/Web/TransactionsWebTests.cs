using System.Net;
using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
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
        var all = new StubHttpHandler(HttpStatusCode.OK, """{"items":[],"total":0,"page":1,"pageSize":50}""");
        var some = new StubHttpHandler(HttpStatusCode.OK, """{"items":[{"id":4,"status":"Failed","processingError":"Unparseable timestamp 'x'","payload":"{}"}],"total":121,"page":2,"pageSize":50}""");

        await new TransactionsApiClient(all.CreateClient()).ListRawAsync(null, null, null, null, null, null, null, 1, 100, CancellationToken.None);
        var result = await new TransactionsApiClient(some.CreateClient()).ListRawAsync(3, 7, "Failed", new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), " 22 01 ", "hiba", 2, 50, CancellationToken.None);

        Assert.Equal("/api/attendance/transactions/raw?page=1&pageSize=100", all.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(
            "/api/attendance/transactions/raw?page=2&pageSize=50&integrationId=3&batchId=7&status=Failed&from=2026-04-01&to=2026-04-30&search=22%2001&employee=hiba",
            some.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal((121, 2, 50, 3), (result.Total, result.Page, result.PageSize, result.PageCount));
        Assert.Equal(("Failed", "Unparseable timestamp 'x'"), (result.Items[0].Status, result.Items[0].ProcessingError));
        Assert.Null(result.Items[0].NormalizedId);
    }

    [Fact]
    public async Task TheClient_AsksForNormalizedPunchesAndTheIntegrationOptions()
    {
        var normalized = new StubHttpHandler(HttpStatusCode.OK, """{"items":[{"id":2,"rawId":9,"eventType":"CheckIn","duplicateStatus":"Unique"}],"total":45,"page":2,"pageSize":20}""");
        var options = new StubHttpHandler(HttpStatusCode.OK, """[{"id":3,"name":"HQ"}]""");

        var result = await new TransactionsApiClient(normalized.CreateClient())
            .ListNormalizedAsync(3, 9, "hiba", "CheckIn", "Unique", "None", new DateTime(2026, 4, 1), null, 2, 20, CancellationToken.None);
        var integrations = await new TransactionsApiClient(options.CreateClient()).ListIntegrationsAsync(CancellationToken.None);

        Assert.Equal(
            "/api/attendance/transactions/normalized?page=2&pageSize=20&integrationId=3&rawId=9&employee=hiba&eventType=CheckIn&duplicate=Unique&flag=None&from=2026-04-01",
            normalized.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal((45, 2, 3), (result.Total, result.Page, result.PageCount));
        Assert.Equal((2, 9, "CheckIn"), (result.Items[0].Id, result.Items[0].RawId, result.Items[0].EventType));
        Assert.Equal("/api/attendance/transactions/integrations", options.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("HQ", integrations.Single().Name);
    }

    [Fact]
    public async Task AForbiddenAnswer_IsNotSwallowed()
    {
        var client = new TransactionsApiClient(new StubHttpHandler(HttpStatusCode.Forbidden).CreateClient());

        await Assert.ThrowsAnyAsync<Exception>(() => client.ListRawAsync(null, null, null, null, null, null, null, 1, 100, CancellationToken.None));
    }

    [Fact]
    public async Task TheRawScreen_PagesOnTheServer_DropsAnUnknownStatusAndPageSize_AndKeepsTheFilterForTheForm()
    {
        var transactions = new FakeTransactions { RawTotal = 121 };
        var controller = new RawTransactionsController(transactions);

        var page = await controller.Index(3, 7, "Failed", null, null, "x", "hiba", 3, 50, CancellationToken.None);
        var model = Assert.IsType<RawTransactionsIndexViewModel>(Assert.IsType<ViewResult>(page).Model);
        await controller.Index(null, null, "Bogus", null, null, null, null, -4, 7, CancellationToken.None);

        Assert.Equal((3, 7, "Failed", "x", "hiba"), (model.IntegrationId, model.BatchId, model.Status, model.Search, model.Employee));
        Assert.Equal(["hiba", null], transactions.RawEmployees);
        Assert.Equal((3, 50, 121), (model.Page, model.PageSize, model.Total));
        Assert.Equal("HQ", Assert.Single(model.Integrations).Name);

        // Asked for page 3 of 50, then (an unknown size and a page before the first) page 1 of the default 20.
        Assert.Equal([(3, 50, "Failed"), (1, 20, null)], transactions.RawAsks);
    }

    [Fact]
    public void TheRawPager_KeepsEveryFilterOnEveryPageLink()
    {
        var model = new RawTransactionsIndexViewModel
        {
            Page = 2,
            PageSize = 25,
            Total = 121,
            IntegrationId = 3,
            BatchId = 7,
            Status = "Failed",
            From = new DateTime(2026, 4, 1),
            To = new DateTime(2026, 4, 30),
            Search = "22 01",
            Employee = "hiba",
        };

        var link = model.Pager.For(3);

        Assert.Equal(
            new Dictionary<string, string?> { ["pageSize"] = "25", ["integrationId"] = "3", ["batchId"] = "7", ["status"] = "Failed", ["from"] = "2026-04-01", ["to"] = "2026-04-30", ["search"] = "22 01", ["employee"] = "hiba", ["page"] = "3" },
            link);
        Assert.Equal((5, 26, 50), (model.Pager.PageCount, model.Pager.FirstItem, model.Pager.LastItem));
    }

    [Fact]
    public async Task TheNormalizedScreen_PagesOnTheServer_DropsEveryUnknownNameAndPageSize()
    {
        var transactions = new FakeTransactions { NormalizedTotal = 45 };
        var controller = new NormalizedTransactionsController(transactions);

        var page = await controller.Index(null, 9, "hiba", "CheckIn", "Nope", "Nope", null, null, 2, 50, CancellationToken.None);
        var model = Assert.IsType<NormalizedTransactionsIndexViewModel>(Assert.IsType<ViewResult>(page).Model);
        await controller.Index(null, null, null, null, null, null, null, null, -1, 7, CancellationToken.None);

        Assert.Equal((9, "hiba", "CheckIn", null, null), (model.RawId, model.Employee, model.EventType, model.Duplicate, model.Flag));
        Assert.Equal((2, 50, 45), (model.Page, model.PageSize, model.Total));

        // Asked for page 2 of 50, then (an unknown size and a page before the first) page 1 of the default 20.
        Assert.Equal([(2, 50), (1, 20)], transactions.NormalizedAsks);
    }

    [Fact]
    public void TheNormalizedPager_KeepsEveryFilterOnEveryPageLink()
    {
        var model = new NormalizedTransactionsIndexViewModel
        {
            Page = 1,
            PageSize = 10,
            Total = 45,
            IntegrationId = 3,
            RawId = 9,
            Employee = "hiba",
            EventType = "CheckIn",
            Duplicate = "Unique",
            Flag = "None",
            From = new DateTime(2026, 4, 1),
            To = new DateTime(2026, 4, 30),
        };

        Assert.Equal(
            new Dictionary<string, string?> { ["pageSize"] = "10", ["integrationId"] = "3", ["rawId"] = "9", ["employee"] = "hiba", ["eventType"] = "CheckIn", ["duplicate"] = "Unique", ["flag"] = "None", ["from"] = "2026-04-01", ["to"] = "2026-04-30", ["page"] = "2" },
            model.Pager.For(2));
        Assert.Equal(5, model.Pager.PageCount);
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
        public List<(int Page, int PageSize, string? Status)> RawAsks { get; } = [];

        public int RawTotal { get; set; }

        public List<string?> RawEmployees { get; } = [];

        public List<(int Page, int PageSize)> NormalizedAsks { get; } = [];

        public int NormalizedTotal { get; set; }

        public Task<PagedResult<RawTransactionListItem>> ListRawAsync(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, string? employee, int page, int pageSize, CancellationToken cancellationToken)
        {
            RawAsks.Add((page, pageSize, status));
            RawEmployees.Add(employee);
            return Task.FromResult(new PagedResult<RawTransactionListItem> { Total = RawTotal, Page = page, PageSize = pageSize });
        }

        public Task<PagedResult<NormalizedTransactionListItem>> ListNormalizedAsync(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
        {
            NormalizedAsks.Add((page, pageSize));
            return Task.FromResult(new PagedResult<NormalizedTransactionListItem> { Total = NormalizedTotal, Page = page, PageSize = pageSize });
        }

        public Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransactionIntegrationOption>>([new TransactionIntegrationOption { Id = 3, Name = "HQ" }]);
    }
}
