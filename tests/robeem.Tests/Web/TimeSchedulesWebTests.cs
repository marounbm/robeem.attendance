using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.HR.Schedules;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The time schedule screens: the typed client and the controller.</summary>
public sealed class TimeSchedulesWebTests
{
    [Fact]
    public async Task TheClient_UsesTheRouteOfEveryAction()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var client = new TimeSchedulesApiClient(handler.CreateClient());

        await client.ListAsync(CancellationToken.None);
        await client.CreateAsync(new TimeScheduleInput { Title = "x" }, CancellationToken.None);
        await client.UpdateAsync(4, new TimeScheduleInput { Title = "x" }, CancellationToken.None);
        await client.DeleteAsync(4, CancellationToken.None);
        await client.AddShiftAsync(4, new ScheduleShiftInput(), CancellationToken.None);
        await client.UpdateShiftAsync(4, 5, new ScheduleShiftInput(), CancellationToken.None);
        await client.DeleteShiftAsync(4, 5, CancellationToken.None);
        await client.AddTeamAsync(4, new ScheduleTeamInput(), CancellationToken.None);
        await client.RenameTeamAsync(4, 6, new ScheduleTeamInput(), CancellationToken.None);
        await client.DeleteTeamAsync(4, 6, CancellationToken.None);
        await client.SaveRosterAsync(4, 6, new ScheduleRosterInput(), CancellationToken.None);
        await client.SetTeamMembersAsync(4, 6, new ScheduleMembersInput(), CancellationToken.None);

        Assert.Equal(
            [
                ("GET", "/api/time-schedules"), ("POST", "/api/time-schedules"), ("PUT", "/api/time-schedules/4"), ("DELETE", "/api/time-schedules/4"),
                ("POST", "/api/time-schedules/4/shifts"), ("PUT", "/api/time-schedules/4/shifts/5"), ("DELETE", "/api/time-schedules/4/shifts/5"),
                ("POST", "/api/time-schedules/4/teams"), ("PUT", "/api/time-schedules/4/teams/6"), ("DELETE", "/api/time-schedules/4/teams/6"),
                ("PUT", "/api/time-schedules/4/teams/6/roster"), ("PUT", "/api/time-schedules/4/teams/6/members"),
            ],
            handler.Requests.Select(r => (r.Method.Method, r.RequestUri!.PathAndQuery)));
    }

    [Fact]
    public async Task ADetail_IsReadWithItsRoster_AndA404IsNull()
    {
        var detail = """{"id":4,"settings":{"title":"Office","cyclePeriod":7,"firstCycleDay":"Monday"},"shifts":[{"id":5,"title":"Day","startTime":"09:00","endTime":"17:00","totalHours":7}],"teams":[{"id":6,"title":"All","allInclusive":true,"cells":[{"cycleDay":1,"shiftId":5}]}]}""";

        var found = await new TimeSchedulesApiClient(new StubHttpHandler(HttpStatusCode.OK, detail).CreateClient()).GetAsync(4, CancellationToken.None);
        var missing = await new TimeSchedulesApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient()).GetAsync(9, CancellationToken.None);

        Assert.Equal((1, 5), (found!.Teams.Single().Cells.Single().CycleDay, found.Teams.Single().Cells.Single().ShiftId));
        Assert.Equal(7, found.Shifts.Single().TotalHours);
        Assert.Null(missing);
    }

    [Fact]
    public async Task CreateAndRefusals_ReturnTheIdOrTheFieldErrors()
    {
        var problem = """{"title":"x","status":400,"errors":{"Title":["Another time schedule has this name."],"id":["It is still used."]}}""";
        var created = new TimeSchedulesApiClient(new StubHttpHandler(HttpStatusCode.Created, """{"id":5}""").CreateClient());
        var refused = new TimeSchedulesApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, problem, "application/problem+json").CreateClient());

        var ok = await created.CreateAsync(new TimeScheduleInput(), CancellationToken.None);
        var duplicate = await refused.CreateAsync(new TimeScheduleInput(), CancellationToken.None);
        var delete = await refused.DeleteAsync(1, CancellationToken.None);

        Assert.Equal((true, 5), (ok.Succeeded, ok.Id));
        Assert.Equal("Another time schedule has this name.", duplicate.Errors[nameof(TimeScheduleInput.Title)]);
        Assert.Contains("still used", delete.Errors[string.Empty]);
    }

    [Fact]
    public async Task TheRosterGrid_IsPostedAsDayAndShiftPairs_AndAMangledPairIsRefusedWithoutCallingTheApi()
    {
        var schedules = new FakeSchedules();
        var controller = Controller(schedules);

        var saved = (RedirectToActionResult)await controller.SaveRoster(4, 6, ["1:5", "1:8", "3:5"], CancellationToken.None);
        var message = (string)controller.TempData["StatusMessage"]!;
        var mangled = (RedirectToActionResult)await controller.SaveRoster(4, 6, ["1:5", "oops"], CancellationToken.None);
        await controller.SaveRoster(4, 6, null, CancellationToken.None);

        Assert.Equal(("Details", 4), (saved.ActionName, saved.RouteValues!["id"]));
        Assert.Contains("roster was saved", message);
        Assert.Equal(["roster 4/6 1:5,1:8,3:5", "roster 4/6 "], schedules.Calls);
        Assert.Equal("Fill in every required field with a valid value.", controller.TempData["ErrorMessage"]);
        Assert.Equal("Details", mangled.ActionName);
    }

    [Fact]
    public async Task TeamMembers_AreThePostedEmployeeIds_AndNoneEmptiesTheTeam()
    {
        var schedules = new FakeSchedules();
        var controller = Controller(schedules);

        await controller.SetMembers(4, 6, [5, 7], CancellationToken.None);
        await controller.SetMembers(4, 6, null, CancellationToken.None);

        Assert.Equal(["members 4/6 5,7", "members 4/6 "], schedules.Calls);
    }

    [Fact]
    public async Task Creating_GoesToTheNewSchedule_OrShowsTheErrorsOnTheForm()
    {
        var schedules = new FakeSchedules { Save = ApiSaveResult.Success(9) };
        var controller = Controller(schedules);
        var model = new TimeScheduleFormViewModel { Input = new TimeScheduleInput { Title = "Office" } };

        var created = (RedirectToActionResult)await controller.Create(model, CancellationToken.None);
        schedules.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(TimeScheduleInput.CycleStartDate)] = "Give the date that is day 1." });
        var again = await controller.Create(model, CancellationToken.None);

        Assert.Equal(("Details", 9), (created.ActionName, created.RouteValues!["id"]));
        Assert.IsType<ViewResult>(again);
        Assert.Equal("Give the date that is day 1.", controller.ModelState["Input.CycleStartDate"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task ARefusedChange_IsAnErrorBanner_ASuccessIsAStatus_AndAnUnknownScheduleIsNotFound()
    {
        var schedules = new FakeSchedules { Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "The roster uses this shift on 2 day(s)." }) };
        var controller = Controller(schedules);

        await controller.DeleteShift(4, 5, CancellationToken.None);
        var error = (string)controller.TempData["ErrorMessage"]!;
        schedules.Save = ApiSaveResult.Success(5);
        await controller.DeleteShift(4, 5, CancellationToken.None);
        schedules.Save = ApiSaveResult.Missing();

        Assert.Contains("roster uses this shift", error);
        Assert.Equal("The shift was deleted.", controller.TempData["StatusMessage"]);
        Assert.IsType<NotFoundResult>(await controller.DeleteShift(4, 5, CancellationToken.None));
    }

    [Fact]
    public async Task DeletingASchedule_GoesBackToTheList_OrStaysOnItWithTheReason()
    {
        var schedules = new FakeSchedules { Save = ApiSaveResult.Success(4) };
        var controller = Controller(schedules);

        var deleted = (RedirectToActionResult)await controller.Delete(4, CancellationToken.None);
        schedules.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "2 employee(s) are assigned." });
        var refused = (RedirectToActionResult)await controller.Delete(4, CancellationToken.None);

        Assert.Equal("Index", deleted.ActionName);
        Assert.Equal(("Details", 4), (refused.ActionName, refused.RouteValues!["id"]));
        Assert.Equal("2 employee(s) are assigned.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task TheDetails_UnknownIsNotFound_AndKnownIsShown()
    {
        var schedules = new FakeSchedules();
        var controller = Controller(schedules);

        var shown = (ViewResult)await controller.Details(4, CancellationToken.None);
        schedules.Detail = null;

        Assert.Equal(4, ((TimeScheduleDetailsViewModel)shown.Model!).Schedule.Id);
        Assert.IsType<NotFoundResult>(await controller.Details(9, CancellationToken.None));
    }

    [Theory]
    [InlineData("Monday", 1, "Monday")]
    [InlineData("Monday", 3, "Wednesday")]
    [InlineData("Monday", 7, "Sunday")]
    [InlineData("Saturday", 1, "Saturday")]
    [InlineData("Saturday", 3, "Monday")]
    [InlineData("Sunday", 2, "Monday")]
    public void ADayOfAWeeklyPattern_IsNamedFromTheWeekdayTheWeekStartsOn(string first, int day, string expected)
    {
        var model = new TimeScheduleDetailsViewModel { Schedule = new TimeScheduleDetail { Settings = new TimeScheduleInput { CyclePeriod = 7, FirstCycleDay = first } } };

        Assert.Equal(expected, model.DayLabel(day));
    }

    [Fact]
    public void ADayOfAnyOtherPattern_IsJustNumbered()
    {
        var model = new TimeScheduleDetailsViewModel { Schedule = new TimeScheduleDetail { Settings = new TimeScheduleInput { CyclePeriod = 14, FirstCycleDay = "Monday" } } };

        Assert.Equal(("Day 3", false), (model.DayLabel(3), model.IsWeekly));
    }

    [Fact]
    public void TheTimeScheduleScreen_IsForHrOnly()
    {
        string[] Controllers(params string[] roles) => NavigationMenu.For(new FakeActor(roles)).SelectMany(s => s.Items).Select(i => i.Controller).ToArray();

        Assert.Contains("TimeSchedules", Controllers("HRAdministrator"));
        Assert.DoesNotContain("TimeSchedules", Controllers("Employee", "Manager", "PayrollAdministrator", "IntegrationAdministrator"));
    }

    [Fact]
    public void TheRosterBody_CarriesNoTenantOrActorFields()
    {
        // What the Web sends is what the user typed; who they are and which tenant come from headers and the token.
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new ScheduleRosterInput { Cells = [new ScheduleRosterCell { CycleDay = 1, ShiftId = 2 }] }, JsonSerializerOptions.Web));

        Assert.Equal(["cells"], body.RootElement.EnumerateObject().Select(p => p.Name));
    }

    private static TimeSchedulesController Controller(FakeSchedules schedules) =>
        new(schedules) { TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()) };

    private sealed class NullTempDataProvider : ITempDataProvider
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

        public bool IsInRole(string role) => _roles.Contains(role);

        public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
    }

    private sealed class FakeSchedules : ITimeSchedulesApiClient
    {
        public List<string> Calls { get; } = [];

        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public TimeScheduleDetail? Detail { get; set; } = new() { Id = 4 };

        public Task<IReadOnlyList<TimeScheduleListItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TimeScheduleListItem>>([]);

        public Task<TimeScheduleDetail?> GetAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Detail);

        public Task<ApiSaveResult> CreateAsync(TimeScheduleInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> UpdateAsync(int id, TimeScheduleInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> AddShiftAsync(int id, ScheduleShiftInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> UpdateShiftAsync(int id, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> DeleteShiftAsync(int id, int shiftId, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> AddTeamAsync(int id, ScheduleTeamInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> RenameTeamAsync(int id, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> DeleteTeamAsync(int id, int teamId, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> SaveRosterAsync(int id, int teamId, ScheduleRosterInput input, CancellationToken cancellationToken)
        {
            Calls.Add($"roster {id}/{teamId} {string.Join(",", input.Cells.Select(c => $"{c.CycleDay}:{c.ShiftId}"))}");
            return Task.FromResult(Save);
        }

        public Task<ApiSaveResult> SetTeamMembersAsync(int id, int teamId, ScheduleMembersInput input, CancellationToken cancellationToken)
        {
            Calls.Add($"members {id}/{teamId} {string.Join(",", input.EmployeeIds)}");
            return Task.FromResult(Save);
        }
    }
}
