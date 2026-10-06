using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Application.HR.Schedules;
using robeem.Contracts.HR.Schedules;
using robeem.DataModel;

namespace robeem.Tests.HR;

/// <summary>Defining time schedules (the schedule screen): the rules of the pattern, shifts, teams and roster, and that attendance resolves what was defined.</summary>
public sealed class TimeScheduleServiceTests : IDisposable
{
    private const string DefaultTeam = TimeScheduleService.DefaultTeamTitle;

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ASchedule_IsCreatedWithItsTeamForEveryone_AnchoredToAWeekday_AndAudited()
    {
        var result = await Run(s => s.CreateAsync(new TimeScheduleInput { Title = "  Office  ", Description = " Mon to Fri ", FirstCycleDay = "sunday" }, CancellationToken.None));

        Assert.True(result.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var schedule = db.TimeSchedules.Single();
        Assert.Equal(("Office", "Mon to Fri", 7, DayOfWeek.Sunday, DateTime.MinValue), (schedule.Title, schedule.Description, schedule.CyclePeriod, schedule.FirstCycleDay, schedule.LastScheduleDate));
        var team = db.TimeScheduleTeams.Single();
        Assert.Equal((DefaultTeam, true, schedule.Id), (team.Title, team.AllInclusive, team.TimeScheduleId));

        var entry = Assert.Single(db.AttendanceAuditLogs);
        Assert.Equal((ChangeAuditActions.ScheduleChanged, "Schedule", schedule.Id, "employee:7"), (entry.Action, entry.TargetType, entry.TargetId, entry.Actor));
        Assert.Contains("week starts on Sunday", entry.Details);
    }

    [Theory]
    [InlineData("", 7, "Monday", null, "Title", "required")]
    [InlineData("X", 0, "Monday", null, "CyclePeriod", "between 1 and 56")]
    [InlineData("X", 57, "Monday", null, "CyclePeriod", "between 1 and 56")]
    [InlineData("X", 7, "Funday", null, "FirstCycleDay", "weekday")]
    [InlineData("X", 14, "Monday", null, "CycleStartDate", "day 1")]
    public async Task ABadSchedule_IsRefused(string title, int period, string weekday, string? start, string field, string fragment)
    {
        var result = await Run(s => s.CreateAsync(new TimeScheduleInput { Title = title, CyclePeriod = period, FirstCycleDay = weekday, CycleStartDate = start is null ? null : DateTime.Parse(start) }, CancellationToken.None));

        Assert.Contains(fragment, result.Errors[field]);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.TimeSchedules);
    }

    [Fact]
    public async Task AScheduleName_IsUnique_IgnoringCase()
    {
        await Run(s => s.CreateAsync(new TimeScheduleInput { Title = "Office" }, CancellationToken.None));

        var again = await Run(s => s.CreateAsync(new TimeScheduleInput { Title = "OFFICE" }, CancellationToken.None));
        var other = await Run(s => s.CreateAsync(new TimeScheduleInput { Title = "Factory" }, CancellationToken.None));
        var rename = await Run(s => s.UpdateAsync(other.Id!.Value, new TimeScheduleInput { Title = "office" }, CancellationToken.None));
        var keep = await Run(s => s.UpdateAsync(other.Id!.Value, new TimeScheduleInput { Title = "FACTORY" }, CancellationToken.None));

        Assert.Contains("has this name", again.Errors[nameof(TimeScheduleInput.Title)]);
        Assert.Contains("has this name", rename.Errors[nameof(TimeScheduleInput.Title)]);
        Assert.True(keep.Succeeded);
    }

    [Fact]
    public async Task ATwoWeekPattern_IsAnchoredToItsDayOne_SoAttendanceCountsPastDaySeven()
    {
        var id = (await Run(s => s.CreateAsync(new TimeScheduleInput { Title = "Rotation", CyclePeriod = 14, CycleStartDate = new DateTime(2026, 10, 5, 15, 30, 0) }, CancellationToken.None))).Id!.Value;

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var schedule = db.TimeSchedules.Single(s => s.Id == id);

        Assert.Equal((new DateTime(2026, 10, 5), 1), (schedule.LastScheduleDate, schedule.LastScheduleDay));
        Assert.Equal(DayOfWeek.Monday, schedule.FirstCycleDay);
        Assert.Equal(
            [(new DateTime(2026, 10, 5), 1), (new DateTime(2026, 10, 12), 8), (new DateTime(2026, 10, 18), 14), (new DateTime(2026, 10, 19), 1), (new DateTime(2026, 10, 4), 14)],
            new[] { new DateTime(2026, 10, 5), new DateTime(2026, 10, 12), new DateTime(2026, 10, 18), new DateTime(2026, 10, 19), new DateTime(2026, 10, 4) }.Select(d => (d, schedule.GetCycleDay(d))));

        var detail = (await Run(s => s.GetAsync(id, CancellationToken.None)))!;
        Assert.Equal((14, new DateTime(2026, 10, 5)), (detail.Settings.CyclePeriod, detail.Settings.CycleStartDate));
    }

    [Fact]
    public async Task AShift_IsKeptAsClockTimes_WithItsPaidHours_AndOvernightIsRecognised()
    {
        var id = await NewScheduleAsync();

        var day = await Run(s => s.AddShiftAsync(id, Shift("Day", "9:00", "17:30", breakTime: true, "13:00", "14:00"), CancellationToken.None));
        var night = await Run(s => s.AddShiftAsync(id, Shift("Night", "22:00", "06:00", breakTime: true, "02:00", "03:00"), CancellationToken.None));
        var plain = await Run(s => s.AddShiftAsync(id, Shift("Plain", "08:00", "12:00", breakTime: false, "10:00", "10:30"), CancellationToken.None));

        Assert.True(day.Succeeded && night.Succeeded && plain.Succeeded);
        var shifts = (await Run(s => s.GetAsync(id, CancellationToken.None)))!.Shifts.ToDictionary(s => s.Title);
        Assert.Equal(("09:00", "17:30", 7.5, false), (shifts["Day"].StartTime, shifts["Day"].EndTime, shifts["Day"].TotalHours, shifts["Day"].IsOvernight));
        Assert.Equal((7.0, true), (shifts["Night"].TotalHours, shifts["Night"].IsOvernight));

        // No break: the times of the unticked break are not stored.
        Assert.Equal((false, "00:00", "00:00", 4.0), (shifts["Plain"].BreakTime, shifts["Plain"].BreakStartTime, shifts["Plain"].BreakEndTime, shifts["Plain"].TotalHours));
    }

    [Theory]
    [InlineData("", "09:00", "17:00", false, "13:00", "14:00", "Title", "required")]
    [InlineData("S", "25:00", "17:00", false, "13:00", "14:00", "StartTime", "time of day")]
    [InlineData("S", "09:00", "later", false, "13:00", "14:00", "EndTime", "time of day")]
    [InlineData("S", "09:00", "09:00", false, "13:00", "14:00", "EndTime", "same time")]
    [InlineData("S", "09:00", "17:00", true, "07:00", "08:00", "BreakStartTime", "inside the shift")]
    [InlineData("S", "09:00", "17:00", true, "13:00", "18:00", "BreakStartTime", "inside the shift")]
    [InlineData("S", "09:00", "17:00", true, "14:00", "13:00", "BreakStartTime", "inside the shift")]
    [InlineData("S", "09:00", "17:00", true, "13:00", "xx", "BreakEndTime", "time of day")]
    [InlineData("S", "22:00", "06:00", true, "12:00", "13:00", "BreakStartTime", "inside the shift")]
    public async Task ABadShift_IsRefused(string title, string start, string end, bool hasBreak, string breakStart, string breakEnd, string field, string fragment)
    {
        var id = await NewScheduleAsync();

        var result = await Run(s => s.AddShiftAsync(id, Shift(title, start, end, hasBreak, breakStart, breakEnd), CancellationToken.None));

        Assert.Contains(fragment, result.Errors[field]);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.TimeScheduleShifts);
    }

    [Fact]
    public async Task ABreakMayEndWhereTheShiftEnds_AndAShiftNameIsUniquePerSchedule()
    {
        var id = await NewScheduleAsync();
        var other = await NewScheduleAsync("Other");

        var lastHour = await Run(s => s.AddShiftAsync(id, Shift("Half", "09:00", "17:00", true, "16:00", "17:00"), CancellationToken.None));
        var duplicate = await Run(s => s.AddShiftAsync(id, Shift("HALF", "08:00", "12:00", false, "00:00", "00:00"), CancellationToken.None));
        var sameNameElsewhere = await Run(s => s.AddShiftAsync(other, Shift("Half", "08:00", "12:00", false, "00:00", "00:00"), CancellationToken.None));

        Assert.True(lastHour.Succeeded);
        Assert.Contains("has this name", duplicate.Errors[nameof(ScheduleShiftInput.Title)]);
        Assert.True(sameNameElsewhere.Succeeded);
    }

    [Fact]
    public async Task AShift_IsChanged_AndAShiftOfAnotherScheduleIsNotFound()
    {
        var id = await NewScheduleAsync();
        var other = await NewScheduleAsync("Other");
        var shiftId = (await Run(s => s.AddShiftAsync(id, Shift("Day", "09:00", "17:00", false, "00:00", "00:00"), CancellationToken.None))).Id!.Value;

        var changed = await Run(s => s.UpdateShiftAsync(id, shiftId, Shift("Early", "07:00", "15:00", true, "11:00", "11:30"), CancellationToken.None));
        var wrongSchedule = await Run(s => s.UpdateShiftAsync(other, shiftId, Shift("X", "07:00", "15:00", false, "00:00", "00:00"), CancellationToken.None));

        Assert.True(changed.Succeeded);
        Assert.True(wrongSchedule.NotFound);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var shift = db.TimeScheduleShifts.Single();
        Assert.Equal(("Early", "07:00", "15:00", true, "11:00", "11:30"), (shift.Title, shift.StartTime, shift.EndTime, shift.BreakTime, shift.BreakStartTime, shift.BreakEndTime));
        Assert.Equal(2, db.AttendanceAuditLogs.Count(a => a.TargetType == "Shift"));
    }

    [Fact]
    public async Task TheRoster_IsSavedPerTeam_ReplacedAsAWhole_AndAnUnchangedSaveWritesNothing()
    {
        var (id, teamId, day, night) = await ScheduleWithShiftsAsync();

        await Run(s => s.SaveRosterAsync(id, teamId, Roster((1, day), (2, day), (3, night)), CancellationToken.None));
        var again = await Run(s => s.SaveRosterAsync(id, teamId, Roster((3, night), (2, day), (1, day)), CancellationToken.None));
        var auditsAfterSame = Count(db => db.AttendanceAuditLogs.Count(a => a.Details!.Contains("Roster")));
        await Run(s => s.SaveRosterAsync(id, teamId, Roster((1, day), (4, night)), CancellationToken.None));

        Assert.True(again.Succeeded);
        Assert.Equal(1, auditsAfterSame);
        var cells = (await Run(s => s.GetAsync(id, CancellationToken.None)))!.Teams.Single().Cells;
        Assert.Equal([(1, day), (4, night)], cells.Select(c => (c.CycleDay, c.ShiftId)));
        Assert.Equal(2, Count(db => db.AttendanceAuditLogs.Count(a => a.Details!.Contains("Roster"))));

        var empty = await Run(s => s.SaveRosterAsync(id, teamId, new ScheduleRosterInput(), CancellationToken.None));
        Assert.True(empty.Succeeded);
        Assert.Equal(0, Count(db => db.TimeScheduleLines.Count()));
    }

    [Fact]
    public async Task ARoster_AllowsASplitShift_ButNotOverlappingOnes_NorADayOutsideThePatternNorAForeignShift()
    {
        var (id, teamId, day, _) = await ScheduleWithShiftsAsync();
        var morning = (await Run(s => s.AddShiftAsync(id, Shift("Morning", "06:00", "10:00", false, "00:00", "00:00"), CancellationToken.None))).Id!.Value;
        var evening = (await Run(s => s.AddShiftAsync(id, Shift("Evening", "16:00", "20:00", false, "00:00", "00:00"), CancellationToken.None))).Id!.Value;
        var otherSchedule = await NewScheduleAsync("Other");
        var foreign = (await Run(s => s.AddShiftAsync(otherSchedule, Shift("Foreign", "09:00", "17:00", false, "00:00", "00:00"), CancellationToken.None))).Id!.Value;

        var split = await Run(s => s.SaveRosterAsync(id, teamId, Roster((1, morning), (1, evening)), CancellationToken.None));
        var overlap = await Run(s => s.SaveRosterAsync(id, teamId, Roster((2, day), (2, morning)), CancellationToken.None));
        var outside = await Run(s => s.SaveRosterAsync(id, teamId, Roster((8, day)), CancellationToken.None));
        var zero = await Run(s => s.SaveRosterAsync(id, teamId, Roster((0, day)), CancellationToken.None));
        var notOurs = await Run(s => s.SaveRosterAsync(id, teamId, Roster((1, foreign)), CancellationToken.None));

        Assert.True(split.Succeeded);
        Assert.Contains("overlap", overlap.Errors[nameof(ScheduleRosterInput.Cells)]);
        Assert.Contains("outside", outside.Errors[nameof(ScheduleRosterInput.Cells)]);
        Assert.Contains("outside", zero.Errors[nameof(ScheduleRosterInput.Cells)]);
        Assert.Contains("doesn't belong", notOurs.Errors[nameof(ScheduleRosterInput.Cells)]);

        // The refused saves left the split shift alone.
        Assert.Equal([(1, morning), (1, evening)], (await Run(s => s.GetAsync(id, CancellationToken.None)))!.Teams.Single().Cells.Select(c => (c.CycleDay, c.ShiftId)));
    }

    [Fact]
    public async Task APatternCanNotBeShortenedPastTheRoster_AndAShiftOnTheRosterCanNotBeDeleted()
    {
        var (id, teamId, day, night) = await ScheduleWithShiftsAsync();
        await Run(s => s.SaveRosterAsync(id, teamId, Roster((6, day)), CancellationToken.None));

        var shorter = await Run(s => s.UpdateAsync(id, new TimeScheduleInput { Title = "Office", CyclePeriod = 5, CycleStartDate = new DateTime(2026, 10, 5) }, CancellationToken.None));
        var deleteUsed = await Run(s => s.DeleteShiftAsync(id, day, CancellationToken.None));
        var deleteFree = await Run(s => s.DeleteShiftAsync(id, night, CancellationToken.None));

        Assert.Contains("day 6", shorter.Errors[nameof(TimeScheduleInput.CyclePeriod)]);
        Assert.Contains("1 day(s)", deleteUsed.Errors["id"]);
        Assert.True(deleteFree.Succeeded);
        Assert.Equal(7, Count(db => db.TimeSchedules.Single().CyclePeriod));

        await Run(s => s.SaveRosterAsync(id, teamId, new ScheduleRosterInput(), CancellationToken.None));
        Assert.True((await Run(s => s.UpdateAsync(id, new TimeScheduleInput { Title = "Office", CyclePeriod = 5, CycleStartDate = new DateTime(2026, 10, 5) }, CancellationToken.None))).Succeeded);
    }

    [Fact]
    public async Task ATeam_IsAdded_Renamed_AndTheTeamForEveryoneCanNotBeDeleted()
    {
        var id = await NewScheduleAsync();
        var everyone = Count(db => db.TimeScheduleTeams.Single().Id);

        var added = await Run(s => s.AddTeamAsync(id, new ScheduleTeamInput { Title = "Weekend" }, CancellationToken.None));
        var duplicate = await Run(s => s.AddTeamAsync(id, new ScheduleTeamInput { Title = "weekend" }, CancellationToken.None));
        var renamed = await Run(s => s.RenameTeamAsync(id, added.Id!.Value, new ScheduleTeamInput { Title = "Weekend cover" }, CancellationToken.None));
        var deleteEveryone = await Run(s => s.DeleteTeamAsync(id, everyone, CancellationToken.None));

        Assert.True(added.Succeeded);
        Assert.Contains("has this name", duplicate.Errors[nameof(ScheduleTeamInput.Title)]);
        Assert.True(renamed.Succeeded);
        Assert.Contains("can't be deleted", deleteEveryone.Errors["id"]);
        var teams = (await Run(s => s.GetAsync(id, CancellationToken.None)))!.Teams;
        Assert.Equal([(DefaultTeam, true), ("Weekend cover", false)], teams.Select(t => (t.Title, t.AllInclusive)));
    }

    [Fact]
    public async Task TeamMembers_AreChosenFromTheEmployeesOnTheSchedule_AndLeaversGoBackToTheTeamForEveryone()
    {
        var id = await NewScheduleAsync();
        var otherSchedule = await NewScheduleAsync("Other");
        var team = (await Run(s => s.AddTeamAsync(id, new ScheduleTeamInput { Title = "Weekend" }, CancellationToken.None))).Id!.Value;
        var (ada, alan, grace) = (AddEmployee("E-1", "Ada"), AddEmployee("E-2", "Alan"), AddEmployee("E-3", "Grace"));
        Assign(ada, id);
        Assign(alan, id);
        Assign(grace, otherSchedule);
        var everyone = Count(db => db.TimeScheduleTeams.Where(t => t.TimeScheduleId == id && t.AllInclusive).Single().Id);

        var both = await Run(s => s.SetTeamMembersAsync(id, team, new ScheduleMembersInput { EmployeeIds = [ada, alan, alan] }, CancellationToken.None));
        var teamOf = () => Count(db => db.TimeScheduleEmployees.Where(a => a.TimeScheduleId == id).OrderBy(a => a.EmployeeId).Select(a => a.TeamId).ToList());
        var afterBoth = teamOf();
        var justAlan = await Run(s => s.SetTeamMembersAsync(id, team, new ScheduleMembersInput { EmployeeIds = [alan] }, CancellationToken.None));
        var afterAlan = teamOf();
        var stranger = await Run(s => s.SetTeamMembersAsync(id, team, new ScheduleMembersInput { EmployeeIds = [grace] }, CancellationToken.None));
        var forEveryone = await Run(s => s.SetTeamMembersAsync(id, everyone, new ScheduleMembersInput { EmployeeIds = [ada] }, CancellationToken.None));

        Assert.True(both.Succeeded && justAlan.Succeeded);
        Assert.Equal([team, team], afterBoth);
        Assert.Equal([everyone, team], afterAlan);
        Assert.Contains("assigned to this schedule", stranger.Errors[nameof(ScheduleMembersInput.EmployeeIds)]);
        Assert.Contains("everyone", forEveryone.Errors[nameof(ScheduleMembersInput.EmployeeIds)]);
        Assert.Equal(2, (await Run(s => s.GetAsync(id, CancellationToken.None)))!.Employees.Count);
    }

    [Fact]
    public async Task DeletingATeam_SendsItsMembersBack_AndRemovesItsRoster()
    {
        var (id, _, day, _) = await ScheduleWithShiftsAsync();
        var team = (await Run(s => s.AddTeamAsync(id, new ScheduleTeamInput { Title = "Weekend" }, CancellationToken.None))).Id!.Value;
        var ada = AddEmployee("E-1", "Ada");
        Assign(ada, id);
        await Run(s => s.SetTeamMembersAsync(id, team, new ScheduleMembersInput { EmployeeIds = [ada] }, CancellationToken.None));
        await Run(s => s.SaveRosterAsync(id, team, Roster((1, day)), CancellationToken.None));

        var deleted = await Run(s => s.DeleteTeamAsync(id, team, CancellationToken.None));

        Assert.True(deleted.Succeeded);
        var everyone = Count(db => db.TimeScheduleTeams.Single().Id);
        Assert.Equal(everyone, Count(db => db.TimeScheduleEmployees.Single().TeamId));
        Assert.Equal(0, Count(db => db.TimeScheduleLines.Count()));
    }

    [Fact]
    public async Task ASchedule_IsDeletedWithEverythingInIt_ButNotWhileEmployeesAreAssigned()
    {
        var (id, teamId, day, _) = await ScheduleWithShiftsAsync();
        await Run(s => s.SaveRosterAsync(id, teamId, Roster((1, day)), CancellationToken.None));
        var ada = AddEmployee("E-1", "Ada");
        Assign(ada, id);

        var assigned = await Run(s => s.DeleteAsync(id, CancellationToken.None));
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.TimeScheduleEmployees.RemoveRange(db.TimeScheduleEmployees);
            db.SaveChanges();
        }

        var deleted = await Run(s => s.DeleteAsync(id, CancellationToken.None));
        var again = await Run(s => s.DeleteAsync(id, CancellationToken.None));

        Assert.Contains("1 employee(s)", assigned.Errors["id"]);
        Assert.True(deleted.Succeeded);
        Assert.True(again.NotFound);
        Assert.Equal((0, 0, 0, 0), Count(db => (db.TimeSchedules.Count(), db.TimeScheduleShifts.Count(), db.TimeScheduleTeams.Count(), db.TimeScheduleLines.Count())));
    }

    [Fact]
    public async Task ASchedule_ThatAPolicyIsSetFor_IsKept_AndASystemScheduleCanNotBeDeleted()
    {
        var id = await NewScheduleAsync();
        var system = await NewScheduleAsync("System");
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.TimeSchedules.Single(s => s.Id == system).SystemSchedule = true;
            db.AttendancePolicies.Add(new AttendancePolicy { Title = "Office rules", TimeScheduleId = id });
            db.SaveChanges();
        }

        var withPolicy = await Run(s => s.DeleteAsync(id, CancellationToken.None));
        var systemResult = await Run(s => s.DeleteAsync(system, CancellationToken.None));

        Assert.Contains("still used", withPolicy.Errors["id"]);
        Assert.Contains("system", systemResult.Errors["id"]);
        Assert.Equal((2, 2), Count(db => (db.TimeSchedules.Count(), db.TimeScheduleTeams.Count())));
    }

    [Fact]
    public async Task TheList_CountsShiftsTeamsAndEmployees_AndTheDetailCarriesEverythingTheScreenShows()
    {
        var (id, teamId, day, _) = await ScheduleWithShiftsAsync();
        await NewScheduleAsync("Another");
        await Run(s => s.SaveRosterAsync(id, teamId, Roster((1, day), (2, day)), CancellationToken.None));
        Assign(AddEmployee("E-1", "Ada"), id);

        var list = await Run(s => s.ListAsync(CancellationToken.None));
        var detail = (await Run(s => s.GetAsync(id, CancellationToken.None)))!;

        Assert.Equal(["Another", "Office"], list.Select(l => l.Title));
        var office = list.Single(l => l.Id == id);
        Assert.Equal((2, 1, 1, 7), (office.ShiftCount, office.TeamCount, office.EmployeeCount, office.CyclePeriod));
        Assert.Equal([("Day", 2), ("Night", 0)], detail.Shifts.Select(s => (s.Title, s.RosterUses)));
        Assert.Equal(1, detail.Teams.Single().EmployeeCount);
        Assert.Equal("Ada Lovelace", Assert.Single(detail.Employees).Name);
        Assert.Null(await Run(s => s.GetAsync(999, CancellationToken.None)));
    }

    [Fact]
    public async Task WhatWasDefined_IsWhatAttendanceResolves_ForAnEmployeeOnTheSchedule()
    {
        var id = (await Run(s => s.CreateAsync(new TimeScheduleInput { Title = "Rotation", CyclePeriod = 14, CycleStartDate = new DateTime(2026, 10, 5) }, CancellationToken.None))).Id!.Value;
        var shift = (await Run(s => s.AddShiftAsync(id, Shift("Day", "09:00", "17:00", true, "13:00", "14:00"), CancellationToken.None))).Id!.Value;
        var team = Count(db => db.TimeScheduleTeams.Single().Id);
        await Run(s => s.SaveRosterAsync(id, team, Roster((1, shift), (2, shift), (9, shift)), CancellationToken.None));
        var ada = AddEmployee("E-1", "Ada");
        Assign(ada, id);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var days = await new ScheduleResolutionService(db).ResolveShiftsInRangeAsync(ada, new DateTime(2026, 10, 5), new DateTime(2026, 10, 18), CancellationToken.None);

        Assert.Equal(
            [(1, true), (2, true), (3, false), (9, true), (14, false)],
            days.Where(d => d.CycleDay is 1 or 2 or 3 or 9 or 14).Select(d => (d.CycleDay!.Value, d.Outcome == ShiftResolutionOutcome.Scheduled)));
        var first = Assert.Single(days[0].Shifts);
        Assert.Equal((new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 5, 17, 0, 0)), (first.StartsAt, first.EndsAt));
    }

    private static ScheduleShiftInput Shift(string title, string start, string end, bool breakTime, string breakStart, string breakEnd) =>
        new() { Title = title, StartTime = start, EndTime = end, BreakTime = breakTime, BreakStartTime = breakStart, BreakEndTime = breakEnd };

    private static ScheduleRosterInput Roster(params (int Day, int Shift)[] cells) =>
        new() { Cells = [.. cells.Select(c => new ScheduleRosterCell { CycleDay = c.Day, ShiftId = c.Shift })] };

    private async Task<T> Run<T>(Func<TimeScheduleService, Task<T>> action)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(new TimeScheduleService(db, new AttendanceChangeAuditTrail(new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System))));
    }

    private T Count<T>(Func<robeem.Infrastructure.Persistence.RobeemDbContext, T> read)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return read(db);
    }

    private async Task<int> NewScheduleAsync(string title = "Office") =>
        (await Run(s => s.CreateAsync(new TimeScheduleInput { Title = title }, CancellationToken.None))).Id!.Value;

    /// <summary>A weekly schedule with a day shift and a night shift, and the id of its team for everyone.</summary>
    private async Task<(int Schedule, int Team, int Day, int Night)> ScheduleWithShiftsAsync()
    {
        var id = await NewScheduleAsync();
        var day = (await Run(s => s.AddShiftAsync(id, Shift("Day", "09:00", "17:00", true, "13:00", "14:00"), CancellationToken.None))).Id!.Value;
        var night = (await Run(s => s.AddShiftAsync(id, Shift("Night", "22:00", "06:00", false, "00:00", "00:00"), CancellationToken.None))).Id!.Value;
        return (id, Count(db => db.TimeScheduleTeams.Single(t => t.TimeScheduleId == id).Id), day, night);
    }

    private int AddEmployee(string number, string first)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var profile = new Profile { EmployeeNumber = number, FirstName = first, LastName = first == "Ada" ? "Lovelace" : "Smith", Title = first };
        db.Profiles.Add(profile);
        db.SaveChanges();
        return profile.Id;
    }

    private void Assign(int employeeId, int scheduleId)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var team = db.TimeScheduleTeams.Where(t => t.TimeScheduleId == scheduleId && t.AllInclusive).OrderBy(t => t.Id).First();
        db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { EmployeeId = employeeId, TimeScheduleId = scheduleId, TeamId = team.Id });
        db.SaveChanges();
    }
}
