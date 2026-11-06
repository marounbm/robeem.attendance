using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The one approval workflow: a correction an employee asked for and a record left for review wait in one queue, are listed by one
/// query (paged in the database) and decided by one service. The engine keeps the review approvals in step with its records.
/// </summary>
public sealed class AttendanceApprovalServiceTests : AttendanceEngineTestBase
{
    private readonly int _managerId;
    private readonly int _otherManagerId;
    private readonly int _hrId;

    public AttendanceApprovalServiceTests()
    {
        _managerId = AddProfile("M-1", "Grace", "Hopper");
        _otherManagerId = AddProfile("M-2", "Katherine", "Johnson");
        _hrId = AddProfile("H-1", "Alan", "Turing");
        SetManager(_managerId);
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task TheQueue_MixesCorrectionsAndReviews_OldestDayFirst_AndNamesTheStepOfEach()
    {
        AssignSchedule(StandardDay);
        AddReview(0, 30, hasShift: true);
        AddCorrection(1, ApprovalWorkflow.Manager);
        AddReview(2, 0, hasShift: false);
        AddCorrection(3, ApprovalWorkflow.HR);

        var manager = await ListAsync(_managerId, isManager: true, isHr: false);
        var hr = await ListAsync(_hrId, isManager: false, isHr: true);
        var both = await ListAsync(_managerId, isManager: true, isHr: true);

        // A manager decides their report's reviews and the corrections whose step is theirs; the HR-only correction is not theirs.
        Assert.Equal([("Review", Monday), ("Correction", Monday.AddDays(1)), ("Review", Monday.AddDays(2))], manager.Select(i => (i.Type, i.AttendanceDate)));
        Assert.All(manager, i => Assert.Equal("Manager", i.AwaitingStep));

        // HR decides every review and the HR-only correction; the correction of a managed employee waits for the manager first.
        Assert.Equal([("Review", Monday), ("Review", Monday.AddDays(2)), ("Correction", Monday.AddDays(3))], hr.Select(i => (i.Type, i.AttendanceDate)));
        Assert.All(hr, i => Assert.Equal("HR", i.AwaitingStep));

        // The employee's manager, who is also HR, sees both queues, each item in the step it is at.
        Assert.Equal(4, both.Count);
        Assert.Equal(["HR", "Manager", "HR", "HR"], both.Select(i => i.AwaitingStep));
    }

    [Fact]
    public async Task AnItem_CarriesTheDetailsOfItsKind_AndOnlyThose()
    {
        AssignSchedule(StandardDay);
        AddReview(0, 30, hasShift: true);
        AddCorrection(1, ApprovalWorkflow.Both, edits: 2);

        var items = await ListAsync(_managerId, isManager: true, isHr: false);

        var review = Assert.Single(items, i => i.Type == "Review");
        Assert.Null(review.Correction);
        Assert.Equal(("Overtime", 30, "Ada Lovelace"), (review.Review!.Reason, review.Review.OvertimeMinutes, review.EmployeeName));
        var correction = Assert.Single(items, i => i.Type == "Correction");
        Assert.Null(correction.Review);
        Assert.Equal(["SupplyPunch", "SupplyPunch"], correction.Correction!.Edits.Select(e => e.Kind));
        Assert.Equal(("Both", null), (correction.Correction.ApprovalWorkflow, correction.Correction.ManagerDecision));
    }

    [Fact]
    public async Task TheQueue_IsPagedInTheDatabase_PagesTogetherAreTheWholeQueueOnceInOrder()
    {
        AddMixedQueue();

        var whole = await PageIdsAsync(1, 20);
        var first = await PageIdsAsync(1, 4);
        var second = await PageIdsAsync(2, 4);
        var third = await PageIdsAsync(3, 4);

        Assert.Equal(10, whole.Count);
        Assert.Equal(whole, first.Concat(second).Concat(third));
        Assert.Equal((4, 4, 2), (first.Count, second.Count, third.Count));

        // Oldest day first, the id breaking ties.
        var items = (await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, null, 1, 20))).Items;
        Assert.Equal(items.OrderBy(i => i.AttendanceDate).ThenBy(i => i.Id).Select(i => i.Id), items.Select(i => i.Id));
    }

    [Fact]
    public async Task APageBeyondTheEnd_AnswersTheLastPage_AndThePageSizeIsClamped()
    {
        AddMixedQueue();

        var beyond = await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, null, 99, 4));
        var before = await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, null, -3, 4));

        Assert.Equal((3, 10, 2), (beyond.Page, beyond.Total, beyond.Items.Count));
        Assert.Equal(1, before.Page);
        Assert.Equal(20, (await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, null, 1, 0))).PageSize);
        Assert.Equal(20, (await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, null, 1, -5))).PageSize);
        Assert.Equal(200, (await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, null, 1, 100000))).PageSize);
    }

    [Fact]
    public async Task TheQueue_CanBeNarrowedByKind_Employee_Dates_AndReason_AndTheTotalFollowsTheFilter()
    {
        var other = AddMixedQueue();

        var corrections = await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, AttendanceApprovalType.Correction, null, 1, 20));
        var reviews = await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, AttendanceApprovalType.Review, null, 1, 20));
        var byEmployee = await PageAsync(_managerId, true, false, new ApprovalFilter(other, null, null, null, null, 1, 20));
        var byDates = await PageAsync(_managerId, true, false, new ApprovalFilter(null, Monday.AddDays(1), Monday.AddDays(3), null, null, 1, 20));
        var overtime = await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, ReviewReason.Overtime, 1, 20));
        var narrowedPage = await PageAsync(_managerId, true, false, new ApprovalFilter(EmployeeId, null, null, AttendanceApprovalType.Review, null, 1, 1));

        Assert.Equal((4, 6), (corrections.Total, reviews.Total));
        Assert.All(corrections.Items, i => Assert.Equal("Correction", i.Type));
        Assert.All(reviews.Items, i => Assert.Equal("Review", i.Type));
        Assert.Equal(3, byEmployee.Total);
        Assert.All(byEmployee.Items, i => Assert.Equal(other, i.EmployeeId));
        Assert.Equal(5, byDates.Total);

        // A review reason names a review, so it leaves the corrections out.
        Assert.Equal(2, overtime.Total);
        Assert.All(overtime.Items, i => Assert.Equal("Overtime", i.Review!.Reason));
        Assert.Equal((4, 1), (narrowedPage.Total, narrowedPage.Items.Count));

        // An employee who is not the manager's report has nothing in the manager's queue.
        Assert.Equal(0, (await PageAsync(_otherManagerId, true, false, new ApprovalFilter(other, null, null, null, null, 1, 20))).Total);
    }

    [Fact]
    public async Task NeverTheApproversOwnReview_AndNothingForSomeoneWhoDecidesNothing()
    {
        AssignSchedule(StandardDay);
        AddReview(0, 30, hasShift: true);

        // The employee, if they were HR, would not see their own record to approve it.
        Assert.Empty(await ListAsync(EmployeeId, isManager: false, isHr: true));
        Assert.Single(await ListAsync(_hrId, isManager: false, isHr: true));
        Assert.Empty(await ListAsync(_managerId, isManager: false, isHr: false));
        Assert.Empty(await ListAsync(_otherManagerId, isManager: true, isHr: false));
    }

    [Fact]
    public async Task ForAnEmployeeWithNoManager_HrSeesTheManagersStep_AsItsOwn()
    {
        SetManager(null);
        AddCorrection(1, ApprovalWorkflow.Manager);

        Assert.Empty(await ListAsync(_managerId, isManager: true, isHr: false));
        var item = Assert.Single(await ListAsync(_hrId, isManager: false, isHr: true));
        Assert.Equal(("Correction", "HR"), (item.Type, item.AwaitingStep));
    }

    [Fact]
    public async Task ACorrection_IsDecidedThroughItsApproval_AndTheDayIsRecalculated()
    {
        var approvalId = await GivenAProposedCorrectionAsync();

        var result = await DecideAsync(approvalId, _managerId, asHr: false, approved: true);

        Assert.True(result.Succeeded);
        Assert.Equal((AttendanceApprovalType.Correction, AttendanceApprovalStatus.Applied, AttendanceExceptionStatus.Closed), (result.Type, result.Status, result.ExceptionStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.False(Assert.Single(db.AttendanceCorrections).IsPending);
        Assert.Equal(AttendanceRecordStatus.Approved, db.AttendanceRecords.Single(r => r.IsCurrent).Status);
        Assert.Empty(await ListAsync(_managerId, isManager: true, isHr: false));
    }

    [Fact]
    public async Task UnderBoth_TheItemMovesFromTheManagersQueueToHrs_WithTheStepItIsAt()
    {
        AddDefaultPolicy(p => p.CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.Both);
        var approvalId = await GivenAProposedCorrectionAsync();
        Assert.Equal("Manager", Assert.Single(await ListAsync(_managerId, isManager: true, isHr: false), i => i.Type == "Correction").AwaitingStep);

        var afterManager = await DecideAsync(approvalId, _managerId, asHr: false, approved: true);

        Assert.Equal(AttendanceApprovalStatus.Pending, afterManager.Status);
        Assert.DoesNotContain(await ListAsync(_managerId, isManager: true, isHr: false), i => i.Type == "Correction");
        Assert.Equal("HR", Assert.Single(await ListAsync(_hrId, isManager: false, isHr: true), i => i.Type == "Correction").AwaitingStep);

        var afterHr = await DecideAsync(approvalId, _hrId, asHr: true, approved: true);
        Assert.Equal(AttendanceApprovalStatus.Applied, afterHr.Status);
    }

    [Fact]
    public async Task ARejectedCorrection_ReopensTheException_AndLeavesTheQueue()
    {
        var approvalId = await GivenAProposedCorrectionAsync();

        var result = await DecideAsync(approvalId, _managerId, asHr: false, approved: false);

        Assert.Equal((AttendanceApprovalStatus.Rejected, AttendanceExceptionStatus.Open), (result.Status, result.ExceptionStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceExceptionStatus.Open, Assert.Single(db.AttendanceExceptions).Status);
        Assert.DoesNotContain(await ListAsync(_managerId, isManager: true, isHr: false), i => i.Type == "Correction");
    }

    [Fact]
    public async Task AReview_IsDecidedThroughItsApproval_TheRecordMoves_AndPayrollReadsTheApproval()
    {
        await WorkOvertimeAsync();
        var approval = PendingReviewApproval();
        Assert.Equal((AttendanceApprovalType.Review, ApprovalWorkflow.ManagerOrHr, 90), (approval.Type, approval.Workflow, approval.OvertimeMinutes));
        Assert.Empty(await ExtractAsync());

        var result = await DecideAsync(approval.Id, _managerId, asHr: false, approved: true);

        Assert.True(result.Succeeded);
        Assert.Equal((AttendanceApprovalStatus.Approved, AttendanceRecordStatus.Approved), (result.Status, result.RecordStatus));
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var decided = Assert.Single(db.AttendanceApprovals);
        Assert.Equal((_managerId, null), (decided.ManagerDecidedById, decided.HrDecision));
        Assert.Single(await ExtractAsync());
    }

    [Fact]
    public async Task ADecisionIsRefused_ForAnUnknownApproval_ADecidedOne_AndAnApproverWhoIsNotTheEmployeesManager()
    {
        await WorkOvertimeAsync();
        var approvalId = PendingReviewApproval().Id;

        Assert.True((await DecideAsync(9999, _managerId, asHr: false, approved: true)).NotFound);
        Assert.Contains(CorrectionErrorKeys.DecidedBy, (await DecideAsync(approvalId, _otherManagerId, asHr: false, approved: true)).Errors.Keys);
        Assert.Contains(ApprovalErrorKeys.Reason, (await DecideAsync(approvalId, _managerId, asHr: false, approved: false)).Errors.Keys);

        Assert.True((await DecideAsync(approvalId, _managerId, asHr: false, approved: true)).Succeeded);
        Assert.Contains(ApprovalErrorKeys.Approval, (await DecideAsync(approvalId, _managerId, asHr: false, approved: true)).Errors.Keys);
    }

    [Fact]
    public async Task TheEngine_QueuesOneReview_ForARecordItLeavesForReview_AndSetsItAsideWhenTheDayChanges()
    {
        await WorkOvertimeAsync();
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var queued = Assert.Single(db.AttendanceApprovals);
            Assert.Equal((AttendanceApprovalStatus.Pending, db.AttendanceRecords.Single(r => r.IsCurrent).Id), (queued.Status, queued.AttendanceRecordId));
        }

        // Processing the same day again changes nothing: still one.
        await ProcessAsync(Monday);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Single(db.AttendanceApprovals);
        }

        // A later check-out is a new version: the old one's approval is set aside, the new one is queued.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.NormalizedAttendanceTransactions.Single(n => n.NormalizedEventType == NormalizedEventType.CheckOut).DuplicateStatus = DuplicateStatus.DuplicateConfirmed;
            db.SaveChanges();
        }

        AddPunch(NormalizedEventType.CheckOut, Monday.At(19, 30));
        await ProcessAsync(Monday);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var approvals = check.AttendanceApprovals.OrderBy(a => a.Id).ToList();
        Assert.Equal([AttendanceApprovalStatus.Superseded, AttendanceApprovalStatus.Pending], approvals.Select(a => a.Status));
        Assert.Equal([90, 150], approvals.Select(a => a.OvertimeMinutes));
        Assert.Equal(check.AttendanceRecords.Single(r => r.IsCurrent).Id, approvals[1].AttendanceRecordId);
    }

    [Fact]
    public async Task WhenACorrectionLeavesNothingToReview_TheOldReviewIsSetAside_AndNoneIsQueued()
    {
        await GivenAProposedCorrectionAsync(applyIt: true);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var review = Assert.Single(db.AttendanceApprovals, a => a.Type == AttendanceApprovalType.Review);
        Assert.Equal(AttendanceApprovalStatus.Superseded, review.Status);
        Assert.Equal(AttendanceRecordStatus.Approved, db.AttendanceRecords.Single(r => r.IsCurrent).Status);
        Assert.DoesNotContain(db.AttendanceApprovals, a => a.Type == AttendanceApprovalType.Review && a.Status == AttendanceApprovalStatus.Pending);
    }

    [Fact]
    public async Task TheReconciliation_PutsRightWhatDrifted_AndNeverTouchesADecision()
    {
        await WorkOvertimeAsync();
        var recordId = CurrentRecordId();

        // Drift 1: the pending approval of a record that waits for review is gone.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceApprovals.RemoveRange(db.AttendanceApprovals);
            db.SaveChanges();
        }

        Assert.Equal(new ApprovalReconciliation(1, 0), await ReconcileAsync());
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var queued = Assert.Single(db.AttendanceApprovals);
            Assert.Equal((AttendanceApprovalStatus.Pending, recordId, 90), (queued.Status, queued.AttendanceRecordId, queued.OvertimeMinutes));
        }

        // Nothing drifted now.
        Assert.Equal(new ApprovalReconciliation(0, 0), await ReconcileAsync());

        // Drift 2: the record no longer waits for review, but its approval still does.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceRecords.Single(r => r.Id == recordId).Status = AttendanceRecordStatus.Approved;
            db.SaveChanges();
        }

        Assert.Equal(new ApprovalReconciliation(0, 1), await ReconcileAsync());
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceApprovalStatus.Superseded, Assert.Single(check.AttendanceApprovals).Status);

        // A decision a person made is left as it was.
        var decided = new AttendanceApproval
        {
            Type = AttendanceApprovalType.Review,
            EmployeeId = EmployeeId,
            AttendanceDate = Monday,
            Status = AttendanceApprovalStatus.Approved,
            Workflow = ApprovalWorkflow.ManagerOrHr,
            AttendanceRecordId = recordId,
        };
        check.AttendanceApprovals.Add(decided);
        check.SaveChanges();
        Assert.Equal(new ApprovalReconciliation(0, 0), await ReconcileAsync());
        using var last = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceApprovalStatus.Approved, last.AttendanceApprovals.Single(a => a.Id == decided.Id).Status);
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

    /// <summary>The break-out is missing; the employee proposes 12:00. Returns the correction's approval; with <paramref name="applyIt"/>, the manager has approved it.</summary>
    private async Task<int> GivenAProposedCorrectionAsync(bool applyIt = false)
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);

        int exceptionId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            exceptionId = Assert.Single(db.AttendanceExceptions).Id;
        }

        int correctionId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var proposed = await CorrectionService(db).ProposeCorrectionAsync(exceptionId, Monday.At(12, 0), "The reader missed it.", EmployeeId, CancellationToken.None);
            Assert.True(proposed.Succeeded);
            correctionId = proposed.CorrectionId!.Value;
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var approvalId = check.AttendanceCorrections.Where(c => c.Id == correctionId).Select(c => c.AttendanceApprovalId).Single();
        if (applyIt)
        {
            Assert.True((await DecideAsync(approvalId, _managerId, asHr: false, approved: true)).Succeeded);
        }

        return approvalId;
    }

    private AttendanceCorrectionService CorrectionService(RobeemDbContext db) =>
        new(db, new AttendancePolicyResolver(db), new AttendanceApprovalService(db, CreateEngine(db), CreateAudit(db), Clock), CreateAudit(db), Clock);

    private AttendanceApproval PendingReviewApproval()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceApprovals.Single(a => a.Type == AttendanceApprovalType.Review && a.Status == AttendanceApprovalStatus.Pending);
    }

    private int CurrentRecordId()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceRecords.Single(r => r.IsCurrent).Id;
    }

    private async Task<ApprovalResult> DecideAsync(int approvalId, int decidedBy, bool asHr, bool approved)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceApprovalService(db, CreateEngine(db), CreateAudit(db), Clock).DecideAsync(approvalId, decidedBy, asHr, approved, approved ? null : "  ", CancellationToken.None);
    }

    private async Task<ApprovalReconciliation> ReconcileAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceApprovalReconciler(db, Clock).ReconcileAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<ApprovalListItem>> ListAsync(int approverId, bool isManager, bool isHr) =>
        (await PageAsync(approverId, isManager, isHr, new ApprovalFilter(null, null, null, null, null, 1, 0))).Items;

    private async Task<PagedResult<ApprovalListItem>> PageAsync(int approverId, bool isManager, bool isHr, ApprovalFilter filter)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceApprovalService(db, CreateEngine(db), CreateAudit(db), Clock).ListPendingAsync(approverId, isManager, isHr, filter, CancellationToken.None);
    }

    private async Task<List<int>> PageIdsAsync(int page, int pageSize) =>
        (await PageAsync(_managerId, true, false, new ApprovalFilter(null, null, null, null, null, page, pageSize))).Items.Select(i => i.Id).ToList();

    private int AddProfile(string number, string firstName, string lastName)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var profile = new Profile { EmployeeNumber = number, FirstName = firstName, LastName = lastName, Title = $"{firstName} {lastName}" };
        db.Add(profile);
        db.SaveChanges();
        return profile.Id;
    }

    private void SetManager(int? managerId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = managerId;
        db.SaveChanges();
    }

    /// <summary>
    /// Ten things waiting for the manager, from two employees who report to them: six reviews (two with overtime, two on a day with no
    /// shift, one with the check-out missing, one plain) and four corrections. Returns the second employee's id.
    /// </summary>
    private int AddMixedQueue()
    {
        var other = AddProfile("R-2", "Hedy", "Lamarr");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Single(p => p.Id == other).ManagerId = _managerId;
            db.SaveChanges();
        }

        AssignSchedule(StandardDay);
        AddReview(0, 30, hasShift: true);
        AddReview(1, 0, hasShift: false);
        AddReview(2, 0, hasShift: true, checkOut: false);
        AddReview(3, 0, hasShift: true);
        AddReview(0, 45, hasShift: true, employee: other);
        AddReview(4, 0, hasShift: false, employee: other);
        AddCorrection(0, ApprovalWorkflow.Manager);
        AddCorrection(1, ApprovalWorkflow.Both);
        AddCorrection(2, ApprovalWorkflow.Manager, employee: other);
        AddCorrection(5, ApprovalWorkflow.Manager);
        return other;
    }

    /// <summary>A record left for review, with the pending review approval the engine would have queued for it.</summary>
    private void AddReview(int dayOffset, int overtime, bool hasShift, bool checkOut = true, int? employee = null)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var who = employee ?? EmployeeId;
        var day = Monday.AddDays(dayOffset);
        var record = new AttendanceRecord
        {
            EmployeeId = who,
            AttendanceDate = day,
            TimeScheduleShiftId = hasShift ? db.TimeScheduleShifts.First().Id : null,
            ActualCheckIn = day.AddHours(8),
            ActualCheckOut = checkOut ? day.AddHours(17) : null,
            OvertimeMinutes = overtime,
            Status = AttendanceRecordStatus.PendingReview,
        };
        db.AttendanceRecords.Add(record);
        db.SaveChanges();
        db.AttendanceApprovals.Add(new AttendanceApproval
        {
            Type = AttendanceApprovalType.Review,
            EmployeeId = who,
            AttendanceDate = day,
            Workflow = ApprovalWorkflow.ManagerOrHr,
            AttendanceRecordId = record.Id,
            OvertimeMinutes = overtime,
            RequestedAt = day,
        });
        db.SaveChanges();
    }

    /// <summary>A request to correct a day, with <paramref name="edits"/> punches to add, waiting for its first step.</summary>
    private void AddCorrection(int dayOffset, ApprovalWorkflow workflow, int edits = 1, int? employee = null)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var who = employee ?? EmployeeId;
        var day = Monday.AddDays(dayOffset);
        var approval = new AttendanceApproval
        {
            Type = AttendanceApprovalType.Correction,
            EmployeeId = who,
            AttendanceDate = day,
            Workflow = workflow,
            RequestedByEmployeeId = who,
            RequestedAt = day.AddHours(9),
        };
        db.AttendanceApprovals.Add(approval);
        for (var i = 0; i < edits; i++)
        {
            db.AttendanceCorrections.Add(new AttendanceCorrection
            {
                AttendanceApproval = approval,
                EmployeeId = who,
                AttendanceDate = day,
                Kind = AttendanceCorrectionKind.SupplyPunch,
                CorrectedEventType = i == 0 ? NormalizedEventType.CheckIn : NormalizedEventType.CheckOut,
                ProposedValue = day.AddHours(i == 0 ? 8 : 17),
                TimeZoneId = "UTC",
                Justification = "Forgot",
                RequestedByEmployeeId = who,
            });
        }

        db.SaveChanges();
    }

    private async Task<IReadOnlyList<PayrollAttendanceDay>> ExtractAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new PayrollAttendanceExtract(db, new AttendancePolicyResolver(db)).GetApprovedRecordsAsync(Monday, Monday, CancellationToken.None);
    }
}
