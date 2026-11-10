using System.Net;
using robeem.Contracts.HR.Holidays;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Tests.Web;

public sealed class LeavesWebTests
{
    [Fact]
    public void ThePager_KeepsEveryFilterAndThePageSize_OnEveryPageLink()
    {
        var model = new LeavesIndexViewModel
        {
            EmployeeId = 7,
            Search = "ada",
            From = new DateTime(2026, 1, 1),
            To = new DateTime(2026, 12, 31),
            LeaveType = "Sick",
            Page = 1,
            PageSize = 50,
            Total = 130,
        };

        var link = model.Pager.For(3);

        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["pageSize"] = "50",
                ["employeeId"] = "7",
                ["search"] = "ada",
                ["from"] = "2026-01-01",
                ["to"] = "2026-12-31",
                ["type"] = "Sick",
                ["page"] = "3",
            },
            link);
        Assert.Equal(3, model.Pager.PageCount);
    }

    [Fact]
    public void ThePager_OmitsFiltersThatAreNotSet()
    {
        var link = new LeavesIndexViewModel { Page = 1, PageSize = 20, Total = 100 }.Pager.For(2);

        Assert.Equal(new Dictionary<string, string?> { ["pageSize"] = "20", ["page"] = "2" }, link);
    }

    [Fact]
    public async Task TheLeavesClient_SendsEveryFilter_AndAnEmptySearchIsLeftOut()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"items":[],"total":0,"page":1,"pageSize":20}""");
        var client = new LeavesApiClient(handler.CreateClient());

        await client.ListAsync(7, "ada", new DateTime(2026, 1, 1), new DateTime(2026, 6, 30), "Sick", 2, 50, CancellationToken.None);
        await client.ListAsync(null, " ", null, null, null, 1, 20, CancellationToken.None);

        Assert.Equal("/api/leaves?page=2&pageSize=50&employeeId=7&search=ada&from=2026-01-01&to=2026-06-30&type=Sick", handler.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("/api/leaves?page=1&pageSize=20", handler.Requests[1].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task TheHolidaysClient_AsksForAYear_AndMapsAnErrorOntoTheField()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """[{"id":1,"date":"2026-05-01T00:00:00","name":"Labour Day","isProvisional":false}]""");
        var client = new HolidaysApiClient(handler.CreateClient());

        var holidays = await client.ListAsync(2026, CancellationToken.None);

        Assert.Equal("/api/holidays?year=2026", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(("Labour Day", false), (Assert.Single(holidays).Name, holidays[0].IsProvisional));

        var refused = new HolidaysApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, """{"errors":{"Date":["2026-05-01 already has a holiday (Labour Day)."]}}""").CreateClient());
        var result = await refused.CreateAsync(new HolidayInput { Date = new DateTime(2026, 5, 1), Name = "Again" }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(HolidayInput.Date), result.Errors.Keys);
    }
}
