using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// How a record left PendingReview gets decided (P11 follow-up): overtime under a pre-approval
/// policy, work outside any shift, a day not yet complete. Decisions are additive rows; the record's
/// review status moves, and payroll reads the approving decision of that record version.
/// </summary>
public sealed class AttendanceReviewServiceTests : AttendanceEngineTestBase
{
    private readonly int _managerId;
    private readonly int _otherManagerId;
    private readonly int _hrId;

    public AttendanceReviewServiceTests()
    {
        _managerId = AddProfile("M-1", "Grace", "Hopper");
        _otherManagerId = AddProfile("M-2", "Katherine", "Johnson");
        _hrId = AddProfile("H-1", "Alan", "Turing");

        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = _managerId;
        db.SaveChanges();
    }

    [Fact]
    public async Task AManager_ApprovesOvertime_TheRecordAndTheDayBecomeApproved_AndPayrollPaysIt()
    {
        await WorkOvertimeAsync();
        var record = CurrentRecord();
        Assert.Equal((AttendanceRecordStatus.PendingReview, 90), (record.Status, record.OvertimeMinutes));
        Assert.Empty(await ExtractAsync());

        var result = await DecideAsync(record.Id, _managerId, asHr: false, approved: true);

        Assert.True(result.Succeeded);
        Assert.Equal((AttendanceReviewOutcome.Approved, AttendanceRecordStatus.Approved), (result.Outcome, result.RecordStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var decision = Assert.Single(db.AttendanceReviewDecisions);
        Assert.Equal(
            (record.Id, EmployeeId, Monday, AttendanceReviewerRole.Manager, _managerId, 90),
            (decision.AttendanceRecordId, decision.EmployeeId, decision.AttendanceDate, decision.DecidedByRole, decision.DecidedById, decision.OvertimeMinutes));
        Assert.Equal(AttendanceRecordStatus.Approved, db.AttendanceRecords.Single(r => r.Id == record.Id).Status);
        Assert.Equal(AttendanceRecordStatus.Approved, db.AttendanceDaySummaries.Single().Status);

        // The decision is audited, with the reviewer as the actor.
        var entry = Assert.Single(await AuditAsync());
        Assert.Equal(($"employee:{_managerId}", nameof(AttendanceRecord), record.Id), (entry.Actor, entry.EntityType, entry.EntityId));
        Assert.Contains("Approved by Manager", entry.Details);

        // The calculation itself is untouched, and payroll now pays the day.
        Assert.Equal(TimeSpan.FromMinutes(90), db.AttendanceRecords.Single(r => r.Id == record.Id).OvertimeMinutes * TimeSpan.FromMinutes(1));
        Assert.Single(await ExtractAsync());
    }

    [Fact]
    public async Task ARejection_NeedsAReason_AndKeepsTheDayOutOfPayroll()
    {
        await WorkOvertimeAsync();
        var recordId = CurrentRecord().Id;

        var withoutReason = await DecideAsync(recordId, _managerId, asHr: false, approved: false, reason: "  ");
        Assert.Contains(nameof(DecideReviewRequest.Reason), withoutReason.Errors.Keys);

        var rejected = await DecideAsync(recordId, _managerId, asHr: false, approved: false, reason: "Not authorised.");

        Assert.Equal((AttendanceReviewOutcome.Rejected, AttendanceRecordStatus.Rejected), (rejected.Outcome, rejected.RecordStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("Not authorised.", Assert.Single(db.AttendanceReviewDecisions).Reason);
        Assert.Equal(AttendanceRecordStatus.Rejected, db.AttendanceDaySummaries.Single().Status);
        Assert.Empty(await ExtractAsync());
    }

    [Fact]
    public async Task HrMayReviewAnyEmployee_AndIsRecordedAsHr()
    {
        await WorkOvertimeAsync();

        var result = await DecideAsync(CurrentRecord().Id, _hrId, asHr: true, approved: true);

        Assert.True(result.Succeeded);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceReviewerRole.HR, Assert.Single(db.AttendanceReviewDecisions).DecidedByRole);
    }

    [Fact]
    public async Task ARecordIsRefused_ToAnotherManager_ToItsOwnEmployee_AndToAnUnknownUser()
    {
        await WorkOvertimeAsync();
        var recordId = CurrentRecord().Id;

        Assert.Contains(CorrectionErrorKeys.DecidedBy, (await DecideAsync(recordId, _otherManagerId, asHr: false, approved: true)).Errors.Keys);
        Assert.Contains(CorrectionErrorKeys.DecidedBy, (await DecideAsync(recordId, EmployeeId, asHr: true, approved: true)).Errors.Keys);
        Assert.Contains(CorrectionErrorKeys.DecidedBy, (await DecideAsync(recordId, 9999, asHr: true, approved: true)).Errors.Keys);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceReviewDecisions);
        Assert.Equal(AttendanceRecordStatus.PendingReview, db.AttendanceRecords.Single(r => r.Id == recordId).Status);
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task AnUnknownRecord_IsNotFound_AndADecidedOneIsNotDecidedTwice()
    {
        await WorkOvertimeAsync();
        var recordId = CurrentRecord().Id;
        Assert.True((await DecideAsync(9999, _managerId, asHr: false, approved: true)).NotFound);

        Assert.True((await DecideAsync(recordId, _managerId, asHr: false, approved: true)).Succeeded);
        var again = await DecideAsync(recordId, _managerId, asHr: false, approved: false, reason: "Changed my mind.");

        Assert.Contains("recordId", again.Errors.Keys);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Single(db.AttendanceReviewDecisions);
    }

    [Fact]
    public async Task ARecordWithAnOpenException_CanNotBeReviewed_UntilTheExceptionIsSettled()
    {
        // A missing check-out, long after the grace: the exception is raised and the record needs review.
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(21, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        await ProcessAsync(Monday);
        var recordId = CurrentRecord().Id;

        var refused = await DecideAsync(recordId, _managerId, asHr: false, approved: true);

        Assert.Contains("exception", refused.Errors["recordId"]);
        Assert.Empty(await ListAsync(_managerId, isHr: false));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceReviewDecisions);
    }

    [Fact]
    public async Task ARetiredVersion_CanNotBeReviewed()
    {
        await WorkOvertimeAsync();
        var firstVersion = CurrentRecord().Id;
        ChangeTheDay();
        await ProcessAsync(Monday);

        var result = await DecideAsync(firstVersion, _managerId, asHr: false, approved: true);

        Assert.Contains("replaced", result.Errors["recordId"]);
    }

    [Fact]
    public async Task IfTheDayChangesUnderTheReviewer_NothingIsKept_AndTheyAreAskedToReviewAgain()
    {
        await WorkOvertimeAsync();
        var recordId = CurrentRecord().Id;

        // A late punch arrived after the reviewer opened the record: deciding recalculates the day.
        ChangeTheDay();
        var result = await DecideAsync(recordId, _managerId, asHr: false, approved: true);

        Assert.Contains("changed while it was being reviewed", result.Errors["recordId"]);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceReviewDecisions);
        Assert.Empty(await AuditAsync());
        var current = db.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal((recordId, AttendanceRecordStatus.PendingReview), (current.Id, current.Status));

        // Processing the day for real writes the new version, which is what the reviewer then sees.
        await ProcessAsync(Monday);
        var next = Assert.Single(await ListAsync(_managerId, isHr: false));
        Assert.Equal(150, next.OvertimeMinutes);
        Assert.NotEqual(recordId, next.RecordId);
    }

    [Fact]
    public async Task AnApproval_DoesNotSurviveARecalculationThatChangesTheNumbers()
    {
        await WorkOvertimeAsync();
        Assert.True((await DecideAsync(CurrentRecord().Id, _managerId, asHr: false, approved: true)).Succeeded);
        Assert.Single(await ExtractAsync());

        ChangeTheDay();
        await ProcessAsync(Monday);

        // The new version needs a new decision, and payroll doesn't pay the unseen 2:30 on the old approval.
        Assert.Empty(await ExtractAsync());
        Assert.Equal(AttendanceRecordStatus.PendingReview, CurrentRecord().Status);
        Assert.Equal(150, Assert.Single(await ListAsync(_managerId, isHr: false)).OvertimeMinutes);
    }

    [Fact]
    public async Task TheQueue_IsScopedToTheManager_AndHrSeesEveryone_NeverTheirOwnRecords()
    {
        await WorkOvertimeAsync();

        var theirs = Assert.Single(await ListAsync(_managerId, isHr: false));
        Assert.Equal((EmployeeId, "Overtime", 90), (theirs.EmployeeId, theirs.Reason, theirs.OvertimeMinutes));
        Assert.Empty(await ListAsync(_otherManagerId, isHr: false));
        Assert.Single(await ListAsync(_hrId, isHr: true));

        // The employee, if they were HR, would not see their own record to approve it.
        Assert.Empty(await ListAsync(EmployeeId, isHr: true));
    }

    [Fact]
    public async Task TheQueue_NamesWhyEachRecordNeedsReview()
    {
        // Overtime (Monday) and work on a day off (Tuesday, no shift), and a day still running (Wednesday).
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.RequiresOvertimePreApproval = true);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 30));
        AddPunch(NormalizedEventType.CheckIn, Monday.AddDays(1).At(8, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.AddDays(1).At(12, 0));
        await ProcessAsync(Monday);
        await ProcessAsync(Monday.AddDays(1));

        var reasons = (await ListAsync(_managerId, isHr: false)).OrderBy(i => i.AttendanceDate).Select(i => i.Reason);

        Assert.Equal(["Overtime", "UnscheduledWork"], reasons);
    }

    [Fact]
    public async Task TheQueue_IsPagedInTheDatabase_PagesTogetherAreTheWholeQueueOnceInOrder()
    {
        AddQueue();

        var whole = await PageIdsAsync(1, 20);
        var first = await PageIdsAsync(1, 4);
        var second = await PageIdsAsync(2, 4);

        Assert.Equal(6, whole.Count);
        Assert.Equal(whole, first.Concat(second));
        Assert.Equal((4, 2), (first.Count, second.Count));

        // Oldest day first, the id breaking ties.
        var items = (await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, null, 1, 20))).Items;
        Assert.Equal(items.OrderBy(i => i.AttendanceDate).ThenBy(i => i.RecordId).Select(i => i.RecordId), items.Select(i => i.RecordId));
    }

    [Fact]
    public async Task APageBeyondTheEnd_AnswersTheLastPage_AndThePageSizeIsClamped()
    {
        AddQueue();

        var beyond = await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, null, 99, 4));
        var before = await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, null, -3, 4));

        Assert.Equal((2, 6, 2), (beyond.Page, beyond.Total, beyond.Items.Count));
        Assert.Equal(1, before.Page);
        Assert.Equal(20, (await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, null, 1, 0))).PageSize);
        Assert.Equal(20, (await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, null, 1, -5))).PageSize);
        Assert.Equal(200, (await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, null, 1, 100000))).PageSize);
    }

    [Fact]
    public async Task TheQueue_CanBeNarrowedByEmployee_Dates_AndReason_AndTheTotalFollowsTheFilter()
    {
        var other = AddQueue();

        var byEmployee = await ListPageAsync(_managerId, false, new ReviewFilter(other, null, null, null, 1, 20));
        var byDates = await ListPageAsync(_managerId, false, new ReviewFilter(null, Monday.AddDays(1), Monday.AddDays(3), null, 1, 20));
        var both = await ListPageAsync(_managerId, false, new ReviewFilter(EmployeeId, Monday.AddDays(2), null, null, 1, 20));
        var narrowedPage = await ListPageAsync(_managerId, false, new ReviewFilter(EmployeeId, null, null, null, 1, 1));

        Assert.Equal(2, byEmployee.Total);
        Assert.Equal([Monday, Monday.AddDays(4)], byEmployee.Items.Select(i => i.AttendanceDate));
        Assert.All(byEmployee.Items, i => Assert.Equal(other, i.EmployeeId));
        Assert.Equal(3, byDates.Total);
        Assert.Equal(2, both.Total);
        Assert.Equal((4, 1), (narrowedPage.Total, narrowedPage.Items.Count));

        // An employee who is not the manager's report has nothing in the manager's queue.
        Assert.Equal(0, (await ListPageAsync(_otherManagerId, false, new ReviewFilter(other, null, null, null, 1, 20))).Total);
    }

    [Theory]
    [InlineData(ReviewReason.Overtime, 2)]
    [InlineData(ReviewReason.UnscheduledWork, 2)]
    [InlineData(ReviewReason.IncompleteDay, 1)]
    [InlineData(ReviewReason.Review, 1)]
    public async Task TheQueue_CanBeNarrowedToOneReason_AndEveryRecordShownHasThatReason(ReviewReason reason, int expected)
    {
        AddQueue();

        var page = await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, reason, 1, 20));

        Assert.Equal(expected, page.Total);
        Assert.All(page.Items, i => Assert.Equal(reason.ToString(), i.Reason));
    }

    private async Task WorkOvertimeAsync()
    {
        AssignSchedule(StandardDay);
        AddDefaultPolicy(p => p.RequiresOvertimePreApproval = true);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(18, 30));
        await ProcessAsync(Monday);
    }

    /// <summary>A later check-out replaces the first one: 19:30 instead of 18:30, so 150 minutes of overtime.</summary>
    private void ChangeTheDay()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.NormalizedAttendanceTransactions.Single(n => n.NormalizedEventType == NormalizedEventType.CheckOut).DuplicateStatus = DuplicateStatus.DuplicateConfirmed;
            db.SaveChanges();
        }

        AddPunch(NormalizedEventType.CheckOut, Monday.At(19, 30));
    }

    private AttendanceRecord CurrentRecord()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceRecords.Single(r => r.IsCurrent && r.AttendanceDate == Monday);
    }

    private int AddProfile(string number, string firstName, string lastName)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var profile = new Profile { EmployeeNumber = number, FirstName = firstName, LastName = lastName, Title = $"{firstName} {lastName}" };
        db.Add(profile);
        db.SaveChanges();
        return profile.Id;
    }

    private async Task<ReviewResult> DecideAsync(int recordId, int decidedBy, bool asHr, bool approved, string? reason = null)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).DecideAsync(recordId, decidedBy, asHr, approved, reason, CancellationToken.None);
    }

    private async Task<IReadOnlyList<ReviewListItem>> ListAsync(int approverId, bool isHr) =>
        (await ListPageAsync(approverId, isHr, new ReviewFilter(null, null, null, null, 1, 0))).Items;

    private async Task<PagedResult<ReviewListItem>> ListPageAsync(int approverId, bool isHr, ReviewFilter filter)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).ListPendingAsync(approverId, isHr, filter, CancellationToken.None);
    }

    /// <summary>
    /// Six records waiting for review, of two employees who report to the manager: overtime, a day with no shift, a day with the check-out
    /// missing and a plain one for the first; overtime and a day with no shift for the second. Returns the second employee's id.
    /// </summary>
    private int AddQueue()
    {
        AssignSchedule(StandardDay);
        var other = AddProfile("R-2", "Hedy", "Lamarr");
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.Profiles.Single(p => p.Id == other).ManagerId = _managerId;
        var shiftId = db.TimeScheduleShifts.First().Id;
        var day = Monday.At(0, 0);

        AttendanceRecord Pending(int employeeId, int offset, int overtime, int? shift, bool checkIn, bool checkOut) => new()
        {
            EmployeeId = employeeId,
            AttendanceDate = day.AddDays(offset),
            TimeScheduleShiftId = shift,
            ActualCheckIn = checkIn ? day.AddDays(offset).AddHours(8) : null,
            ActualCheckOut = checkOut ? day.AddDays(offset).AddHours(17) : null,
            OvertimeMinutes = overtime,
            Status = AttendanceRecordStatus.PendingReview,
        };

        db.AttendanceRecords.AddRange(
            Pending(EmployeeId, 0, 30, shiftId, true, true),
            Pending(EmployeeId, 1, 0, null, true, true),
            Pending(EmployeeId, 2, 0, shiftId, true, false),
            Pending(EmployeeId, 3, 0, shiftId, true, true),
            Pending(other, 0, 45, shiftId, true, true),
            Pending(other, 4, 0, null, true, true));
        db.SaveChanges();
        return other;
    }

    private async Task<List<int>> PageIdsAsync(int page, int pageSize) =>
        (await ListPageAsync(_managerId, false, new ReviewFilter(null, null, null, null, page, pageSize))).Items.Select(i => i.RecordId).ToList();

    private async Task<IReadOnlyList<PayrollAttendanceDay>> ExtractAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new PayrollAttendanceExtract(db, new AttendancePolicyResolver(db)).GetApprovedRecordsAsync(Monday, Monday, CancellationToken.None);
    }

    private async Task<IReadOnlyList<AuditLogEntry>> AuditAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return (await CreateAudit(db).ListAsync(AttendanceAuditActions.ReviewDecided, null, null, 1, 100, CancellationToken.None)).Items;
    }

    private AttendanceReviewService CreateService(RobeemDbContext db) => new(db, CreateEngine(db), CreateAudit(db), Clock);
}
