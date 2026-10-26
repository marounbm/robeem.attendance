using System.Net;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Tests.Api;

/// <summary>The raw and normalized punch endpoints: roles, how the filter is read, and how a bad value is refused.</summary>
public sealed class TransactionsEndpointTests : IAsyncDisposable
{
    private readonly FakeTransactions _transactions = new();
    private readonly TestApi _api;

    public TransactionsEndpointTests()
    {
        _api = TestApi.Start(services => services.AddSingleton<IAttendanceTransactionQuery>(_transactions));
    }

    public static IEnumerable<object[]> Endpoints() =>
    [
        ["api/attendance/transactions/raw"],
        ["api/attendance/transactions/normalized"],
        ["api/attendance/transactions/integrations"],
    ];

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_IsForHrAndIntegrationAdministratorsOnly(string url)
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.PayrollAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Get(url, TestApi.As(7, role))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(url, null)).StatusCode);
        Assert.Equal(0, _transactions.Calls);

        Assert.Equal(HttpStatusCode.OK, (await Get(url, TestApi.As(7, AttendanceRoles.HRAdministrator))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(url, TestApi.As(8, AttendanceRoles.IntegrationAdministrator))).StatusCode);
        Assert.Equal(2, _transactions.Calls);
    }

    [Fact]
    public async Task Raw_PassesTheFilterOn_AndRefusesAnUnknownStatus()
    {
        var hr = TestApi.As(7, AttendanceRoles.HRAdministrator);

        var ok = await Get("api/attendance/transactions/raw?integrationId=3&batchId=7&status=failed&from=2026-04-01&to=2026-04-30&search=2200&employee=hiba&page=3&pageSize=20", hr);
        var bare = await Get("api/attendance/transactions/raw", hr);
        var refused = await Get("api/attendance/transactions/raw?status=Bogus", hr);
        var number = await Get("api/attendance/transactions/raw?status=99", hr);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(new RawTransactionFilter(3, 7, RawTransactionProcessingStatus.Failed, new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), "2200", 3, 20) { Employee = "hiba" }, _transactions.RawFilters[0]);

        // Nothing asked for: the first page, and the size left to the service's default.
        Assert.Equal(new RawTransactionFilter(null, null, null, null, null, null, 1, 0), _transactions.RawFilters[1]);
        Assert.Equal(HttpStatusCode.OK, bare.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Pending, Normalized, Failed, Skipped", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, number.StatusCode);
        Assert.Equal(2, _transactions.RawFilters.Count);
    }

    [Fact]
    public async Task Normalized_PassesTheFilterOn_AndRefusesEveryUnknownName()
    {
        var admin = TestApi.As(8, AttendanceRoles.IntegrationAdministrator);

        var ok = await Get("api/attendance/transactions/normalized?integrationId=3&rawId=9&employee=hiba&eventType=checkin&duplicate=DuplicateCandidate&flag=FlaggedInvalidSequence&from=2026-04-01&page=2&pageSize=5", admin);
        var refused = await Get("api/attendance/transactions/normalized?eventType=Nope&duplicate=Nope&flag=Nope", admin);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(
            new NormalizedTransactionFilter(3, 9, "hiba", NormalizedEventType.CheckIn, DuplicateStatus.DuplicateCandidate, TransactionExceptionStatus.FlaggedInvalidSequence, new DateTime(2026, 4, 1), null, 2, 5),
            _transactions.NormalizedFilters.Single());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("eventType", body);
        Assert.Contains("duplicate", body);
        Assert.Contains("flag", body);
    }

    private Task<HttpResponseMessage> Get(string url, string? user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        return _api.Client.SendAsync(request);
    }

    private sealed class FakeTransactions : IAttendanceTransactionQuery
    {
        public int Calls { get; private set; }

        public List<RawTransactionFilter> RawFilters { get; } = [];

        public List<NormalizedTransactionFilter> NormalizedFilters { get; } = [];

        public Task<PagedResult<RawTransactionListItem>> ListRawAsync(RawTransactionFilter filter, CancellationToken cancellationToken)
        {
            Calls++;
            RawFilters.Add(filter);
            return Task.FromResult(new PagedResult<RawTransactionListItem> { Page = filter.Page, PageSize = filter.PageSize });
        }

        public Task<PagedResult<NormalizedTransactionListItem>> ListNormalizedAsync(NormalizedTransactionFilter filter, CancellationToken cancellationToken)
        {
            Calls++;
            NormalizedFilters.Add(filter);
            return Task.FromResult(new PagedResult<NormalizedTransactionListItem> { Page = filter.Page, PageSize = filter.PageSize });
        }

        public Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<TransactionIntegrationOption>>([]);
        }
    }
}
