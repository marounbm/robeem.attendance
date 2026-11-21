using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The related exceptions of one employee's shift on one date are kept together as a group that follows them and is settled as a whole:
/// it waits for a correction, is accepted with one reason, or closes. The exceptions themselves keep their own status.
/// The shift is 08:00-17:00 with a break from 12:00 to 13:00.
/// </summary>
public sealed class AttendanceExceptionGroupTests : AttendanceEngineTestBase
{
    private readonly int _managerId;

    public AttendanceExceptionGroupTests()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var manager = new Profile { EmployeeNumber = "M-1", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
            db.Add(manager);
            db.SaveChanges();
            db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = manager.Id;
            db.SaveChanges();
            _managerId = manager.Id;
        }

        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task TheRelatedExceptionsOfAShift_AreOneGroup_ThatIsOpenWhileTheyAre()
    {
        AddDefaultPolicy(p =>
        {
            p.RaiseExceptionOnLongBreak = true;
            p.RaiseExceptionOnLateBreak = true;
        });
        AddPunches("I@08:00,BO@13:00,BI@14:30,O@17:00");

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var group = Assert.Single(db.AttendanceExceptionGroups);
        var exceptions = db.AttendanceExceptions.OrderBy(e => e.Id).ToList();
        Assert.Equal([AttendanceExceptionType.LongBreak, AttendanceExceptionType.LateBreak], exceptions.Select(e => e.ExceptionType).Order());
        Assert.All(exceptions, e => Assert.Equal(group.Id, e.ExceptionGroupId));
        Assert.Equal((EmployeeId, Monday, AttendanceExceptionStatus.Open), (group.EmployeeId, group.AttendanceDate, group.Status));
        Assert.Equal(exceptions.Min(e => e.DetectedAt), group.DetectedAt);
        Assert.Null(group.ResolvedAt);
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.ExceptionGroupStatusChanged && a.TargetType == nameof(AttendanceExceptionGroup) && a.Details!.StartsWith("(new) -> Open"));
    }

    [Fact]
    public async Task TheExceptionsOfTwoShifts_AreTwoGroups()
    {
        AssignSecondSchedule(new ShiftSpec("06:00", "10:00"), new ShiftSpec("15:00", "19:00"));
        AddPunches("I@06:00,I@15:00");
        Clock.Local = Monday.At(23, 0);

        await ProcessAsync(Monday);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var groups = check.AttendanceExceptionGroups.OrderBy(g => g.TimeScheduleShiftId).ToList();
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Select(g => g.TimeScheduleShiftId).Distinct().Count());
        Assert.All(groups, g => Assert.Equal(AttendanceExceptionStatus.Open, g.Status));
        Assert.All(check.AttendanceExceptions, e => Assert.NotNull(e.ExceptionGroupId));
    }

    [Fact]
    public async Task TheGroupCloses_WhenItsExceptionsDo_TheWayTheLastOneDid()
    {
        AddPunches("I@08:00,BO@12:00,BI@13:00");
        Clock.Local = Monday.At(23, 0);
        await ProcessAsync(Monday);
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));

        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var group = Assert.Single(db.AttendanceExceptionGroups);
        Assert.All(db.AttendanceExceptions, e => Assert.Equal(AttendanceExceptionStatus.Closed, e.Status));
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.AutoResolved), (group.Status, group.ResolutionMethod));
        Assert.NotNull(group.ResolvedAt);
        Assert.Null(group.ResolvedById);
    }

    [Fact]
    public async Task AcceptingTheGroup_ClosesEveryExceptionWithOneReason_AndTheDayIsApproved()
    {
        RaiseBreakExceptions();
        AddPunches("I@08:00,BO@13:00,BI@14:30,O@17:00");
        await ProcessAsync(Monday);
        var groupId = GroupId();

        var accepted = await AcceptAsync(groupId, _managerId, asHr: false, "Covered the phones.");

        Assert.True(accepted.Succeeded);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var group = Assert.Single(db.AttendanceExceptionGroups);
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Accepted, _managerId, "Covered the phones."), (group.Status, group.ResolutionMethod, group.ResolvedById, group.ResolutionNote));
        Assert.NotNull(group.ResolvedAt);
        Assert.Equal(2, db.AttendanceExceptions.Count());
        Assert.All(db.AttendanceExceptions, e => Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Accepted, _managerId, "Covered the phones."), (e.Status, e.ResolutionMethod, e.ResolvedById, e.ResolutionNote)));
        Assert.Equal(AttendanceRecordStatus.Approved, db.AttendanceRecords.Single(r => r.IsCurrent).Status);
        Assert.Contains(db.AttendanceAuditLogs, a => a.TargetType == nameof(AttendanceExceptionGroup) && a.TargetId == group.Id && a.Details!.Contains("accepted by Manager"));

        // The day is calculated again with the same breaks: they stay accepted, and nothing new opens.
        await ProcessAsync(Monday);
        using var again = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(1, again.AttendanceExceptionGroups.Count());
        Assert.Equal(2, again.AttendanceExceptions.Count());
    }

    [Fact]
    public async Task AProblemFoundAfterTheGroupWasAccepted_OpensANewGroup_AndTheOldOneStaysAsHistory()
    {
        RaiseBreakExceptions();
        AddPunches("I@08:00,BO@12:00,BI@13:30,O@17:00");
        await ProcessAsync(Monday);
        await AcceptAsync(GroupId(), _managerId, asHr: false, "Covered the phones.");

        // A different break turns up: it is another problem.
        AddPunch(NormalizedEventType.BreakOut, Monday.At(11, 50));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 40));
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var groups = db.AttendanceExceptionGroups.OrderBy(g => g.Id).ToList();
        Assert.Equal([AttendanceExceptionStatus.Closed, AttendanceExceptionStatus.Open], groups.Select(g => g.Status));
        Assert.Equal(AttendanceExceptionResolutionMethod.Accepted, groups[0].ResolutionMethod);
        Assert.All(db.AttendanceExceptions.Where(e => e.Status == AttendanceExceptionStatus.Open), e => Assert.Equal(groups[1].Id, e.ExceptionGroupId));
    }

    [Fact]
    public async Task OnlyTheEmployeesManagerOrHr_CanAcceptAGroup_NeverTheEmployee_AndAReasonIsRequired()
    {
        RaiseBreakExceptions();
        AddPunches("I@08:00,BO@13:00,BI@14:30,O@17:00");
        await ProcessAsync(Monday);
        var groupId = GroupId();
        int strangerId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var stranger = new Profile { EmployeeNumber = "S-1", FirstName = "Alan", LastName = "Turing", Title = "Alan Turing" };
            db.Add(stranger);
            db.SaveChanges();
            strangerId = stranger.Id;
        }

        var byEmployee = await AcceptAsync(groupId, EmployeeId, asHr: false, "Because I said so.");
        var byStranger = await AcceptAsync(groupId, strangerId, asHr: false, "Not my report.");
        var withoutReason = await AcceptAsync(groupId, _managerId, asHr: false, "  ");
        var byHr = await AcceptAsync(groupId, strangerId, asHr: true, "HR agrees.");

        Assert.Contains("own employee", byEmployee.Errors[CorrectionErrorKeys.DecidedBy]);
        Assert.Contains("manager, or HR", byStranger.Errors[CorrectionErrorKeys.DecidedBy]);
        Assert.Contains("reason is required", withoutReason.Errors[nameof(AcceptExceptionRequest.Reason)]);
        Assert.True(byHr.Succeeded);
    }

    [Fact]
    public async Task AGroupWithAMissingPunch_CannotBeAccepted_AndAGroupIsAcceptedOnlyOnce()
    {
        RaiseBreakExceptions();
        AddPunches("I@08:00,BO@12:00,BI@13:30");
        Clock.Local = Monday.At(23, 0);
        await ProcessAsync(Monday);
        var groupId = GroupId();

        var refused = await AcceptAsync(groupId, _managerId, asHr: false, "Fine.");

        Assert.Contains("can't be accepted as it is", refused.Errors["id"]);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.All(db.AttendanceExceptions, e => Assert.Equal(AttendanceExceptionStatus.Open, e.Status));
            Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(db.AttendanceExceptionGroups).Status);
        }

        // Once the day is complete, only the break exception is left, and that group can be accepted.
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var open = Assert.Single(check.AttendanceExceptionGroups, g => g.Status == AttendanceExceptionStatus.Open);
        Assert.True((await AcceptAsync(open.Id, _managerId, asHr: false, "Fine.")).Succeeded);
        Assert.Contains("closed", (await AcceptAsync(open.Id, _managerId, asHr: false, "Again.")).Errors["id"]);
        Assert.True((await AcceptAsync(99999, _managerId, asHr: false, "Nothing.")).NotFound);
    }

    [Fact]
    public async Task ACorrectionOfTheDay_PutsTheGroupWaiting_ARejectionOpensItAgain_AndAnApprovalClosesIt()
    {
        AddDefaultPolicy(p => p.LateGraceMinutes = 600);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.TimeScheduleShifts.Single().BreakTime = false;
            db.SaveChanges();
        }

        AddPunches("I@08:00,O@13:00,O@13:05,O@17:00");
        await ProcessAsync(Monday);
        var stray = StrayPunchId();

        await ProposeAsync(stray);
        Assert.Equal(AttendanceExceptionStatus.PendingCorrection, Assert.Single(Groups()).Status);

        await DecideAsync(approved: false);
        var reopened = Assert.Single(Groups());
        Assert.Equal((AttendanceExceptionStatus.Open, null), (reopened.Status, reopened.ResolutionMethod));

        await ProposeAsync(stray);
        Assert.Equal(AttendanceExceptionStatus.PendingCorrection, Assert.Single(Groups()).Status);

        await DecideAsync(approved: true);
        var closed = Assert.Single(Groups());
        Assert.Equal((AttendanceExceptionStatus.Closed, AttendanceExceptionResolutionMethod.Correction), (closed.Status, closed.ResolutionMethod));
        using var db2 = Database.CreateContext(TestDatabase.TenantA);
        Assert.All(db2.AttendanceExceptions.Where(e => e.ExceptionGroupId == closed.Id), e => Assert.Equal(AttendanceExceptionStatus.Closed, e.Status));
    }

    [Fact]
    public async Task SyncingADay_GroupsExceptionsThatHaveNone_AndChangesNothingWhenItIsRight()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceExceptions.AddRange(
                NewException(AttendanceExceptionStatus.Open),
                NewException(AttendanceExceptionStatus.PendingCorrection));
            db.SaveChanges();
        }

        await SyncAsync();
        await SyncAsync();

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var group = Assert.Single(check.AttendanceExceptionGroups);
        Assert.Equal(AttendanceExceptionStatus.Open, group.Status);
        Assert.All(check.AttendanceExceptions, e => Assert.Equal(group.Id, e.ExceptionGroupId));
        Assert.Equal(1, check.AttendanceAuditLogs.Count(a => a.TargetType == nameof(AttendanceExceptionGroup)));
    }

    private AttendanceException NewException(AttendanceExceptionStatus status) => new()
    {
        EmployeeId = EmployeeId,
        AttendanceDate = Monday,
        ExceptionType = AttendanceExceptionType.LongBreak,
        Status = status,
        Description = "A long break.",
    };

    private void RaiseBreakExceptions() => AddDefaultPolicy(p =>
    {
        p.RaiseExceptionOnLongBreak = true;
        p.RaiseExceptionOnLateBreak = true;
    });

    private int GroupId()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptionGroups.OrderByDescending(g => g.Id).First().Id;
    }

    private List<AttendanceExceptionGroup> Groups()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceExceptionGroups.OrderBy(g => g.Id).ToList();
    }

    private int StrayPunchId()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.NormalizedAttendanceTransactions.Single(n => n.EventTimestampLocal == Monday.At(13, 5)).Id;
    }

    private async Task<robeem.Application.Common.SaveResult> AcceptAsync(int groupId, int by, bool asHr, string reason)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionGroupService(db, CreateEngine(db), CreateAudit(db), Clock).AcceptAsync(groupId, by, asHr, reason, CancellationToken.None);
    }

    private async Task SyncAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        await new AttendanceExceptionGroupService(db, CreateEngine(db), CreateAudit(db), Clock).SyncDayAsync(EmployeeId, Monday, CancellationToken.None);
    }

    private async Task ProposeAsync(int strayPunchId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = new AttendanceDayCorrectionService(db, new ScheduleResolutionService(db), new AttendanceExceptionQuery(db), new AttendancePolicyResolver(db), new RuleSetProvider(db), CreateAudit(db), Clock);
        var sent = await service.ProposeAsync(
            EmployeeId,
            new DayCorrectionRequest
            {
                Date = Monday,
                Justification = "I clocked out twice for lunch and forgot to clock back in",
                Edits =
                [
                    new DayCorrectionEdit { Action = DayCorrectionRequest.Ignore, TransactionId = strayPunchId },
                    new DayCorrectionEdit { Action = DayCorrectionRequest.Add, EventType = "CheckIn", At = Monday.At(14, 0) },
                ],
            },
            EmployeeId,
            CancellationToken.None);
        Assert.True(sent.Succeeded, string.Join("; ", sent.Errors.Values));
    }

    private async Task DecideAsync(bool approved)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var first = db.AttendanceCorrections.OrderBy(c => c.Id).First(c => c.AttendanceApproval!.Status == AttendanceApprovalStatus.Pending);
        var corrections = new AttendanceCorrectionService(db, new AttendancePolicyResolver(db), new AttendanceApprovalService(db, CreateEngine(db), CreateAudit(db), Clock), CreateAudit(db), Clock);
        await corrections.DecideCorrectionAsync(first.Id, _managerId, approved, isHrDecision: false, CancellationToken.None);
    }

    /// <summary>A second schedule for the employee, replacing the first, with the given shifts on Monday.</summary>
    private void AssignSecondSchedule(params ShiftSpec[] specs)
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.TimeScheduleEmployees.RemoveRange(db.TimeScheduleEmployees);
            db.SaveChanges();
        }

        AssignSchedule(specs);
    }

    private void AddPunches(string punches)
    {
        foreach (var punch in punches.Split(','))
        {
            var parts = punch.Split('@');
            var type = parts[0] switch
            {
                "I" => NormalizedEventType.CheckIn,
                "O" => NormalizedEventType.CheckOut,
                "BO" => NormalizedEventType.BreakOut,
                "BI" => NormalizedEventType.BreakIn,
                _ => throw new ArgumentException($"Unknown punch type {parts[0]}."),
            };
            AddPunch(type, Monday.At(int.Parse(parts[1][..2]), int.Parse(parts[1][3..])));
        }
    }
}
