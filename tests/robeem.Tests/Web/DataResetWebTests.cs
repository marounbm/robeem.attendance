using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

public sealed class DataResetWebTests
{
    [Fact]
    public async Task TheScreenShowsWhatWouldBeDeleted()
    {
        var controller = NewController(new FakeReset());

        var result = await controller.Index(CancellationToken.None);

        var summary = Assert.IsType<AttendanceDataSummary>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(("demo", true), (summary.TenantId, summary.Enabled));
    }

    [Fact]
    public async Task TypingTheTenantId_DeletesAndSaysWhatWentWhileAWrongOneIsRefusedWithTheReason()
    {
        var reset = new FakeReset();
        var controller = NewController(reset);

        var done = await controller.Reset("demo", CancellationToken.None);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(done).ActionName);
        Assert.Equal("demo", reset.Confirmation);
        Assert.Contains("369 raw transactions", (string)controller.TempData["StatusMessage"]!);

        reset.Refusal = "Type the tenant id exactly as shown to confirm.";
        await controller.Reset("nope", CancellationToken.None);
        Assert.Equal("Type the tenant id exactly as shown to confirm.", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public void OnlyAnIntegrationAdministratorSeesTheScreenInTheMenu()
    {
        string[] Controllers(params string[] roles) => NavigationMenu.For(new FakeActor(roles)).SelectMany(s => s.Items).Select(i => i.Controller).ToArray();

        Assert.Contains("DataReset", Controllers(NavigationMenu.Integration));
        Assert.DoesNotContain("DataReset", Controllers(NavigationMenu.Hr));
        Assert.DoesNotContain("DataReset", Controllers(NavigationMenu.Payroll));
    }

    private static DataResetController NewController(FakeReset reset) =>
        new(reset) { TempData = new TempDataDictionary(new DefaultHttpContext(), new NoTempData()) };

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeActor : ICurrentActor
    {
        private readonly string[] _roles;

        public FakeActor(string[] roles)
        {
            _roles = roles;
        }

        public bool HasChoice => true;

        public ActingAs? Choice => new(1, "Test", _roles);

        public ActingAs? Effective => Choice;

        public bool IsInRole(string role) => _roles.Contains(role);

        public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
    }

    private sealed class FakeReset : IDataResetApiClient
    {
        public string? Confirmation { get; private set; }

        public string? Refusal { get; set; }

        public Task<AttendanceDataSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AttendanceDataSummary { TenantId = "demo", Enabled = true });

        public Task<ApiCommandResult<IReadOnlyList<AttendanceDataCount>>> ResetAsync(string confirmation, CancellationToken cancellationToken)
        {
            Confirmation = confirmation;
            return Task.FromResult(Refusal is null
                ? ApiCommandResult<IReadOnlyList<AttendanceDataCount>>.Ok([new AttendanceDataCount { Name = "Raw transactions", Count = 369 }, new AttendanceDataCount { Name = "Exceptions", Count = 0 }])
                : ApiCommandResult<IReadOnlyList<AttendanceDataCount>>.Refused([Refusal]));
        }
    }
}
