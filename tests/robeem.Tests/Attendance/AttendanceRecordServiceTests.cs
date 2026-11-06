using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;
using RecordSortField = robeem.Contracts.Attendance.RecordSortField;

namespace robeem.Tests.Attendance;

/// <summary>The records screens' queries (screens phase 5): scope, filters, a day in full, and reprocessing.</summary>
public sealed class AttendanceRecordServiceTests : AttendanceEngineTestBase
{
    private static readonly RecordFilter Everything = new(null, null, null, null, false, 1, 100);

    [Fact]
    public async Task HrSeesEveryDay_AnEmployeeOnlyTheirOwn_AManagerOnlyTheirReports()
    {
        var (managerId, otherId) = AddPeople();
        await ProcessFullDayAsync();
        await ProcessOtherDayAsync(otherId);

        var hr = await ListAsync(Everything, new ExceptionViewer(900, false, false, true));
        var ada = await ListAsync(Everything, new ExceptionViewer(EmployeeId, true, false, false));
        var manager = await ListAsync(Everything, new ExceptionViewer(managerId, false, true, false));
        var stranger = await ListAsync(Everything, new ExceptionViewer(901, true, true, false));

        Assert.Equal(2, hr.Count);
        Assert.Equal([EmployeeId], ada.Select(d => d.EmployeeId));
        Assert.Equal([EmployeeId], manager.Select(d => d.EmployeeId));
        Assert.Empty(stranger);
    }

    [Fact]
    public async Task TheList_CarriesTheRollup_AndFiltersByDateEmployeeStatusAndExceptions()
    {
        var (_, otherId) = AddPeople();
        await ProcessFullDayAsync();
        AddPunch(NormalizedEventType.CheckOut, Monday.AddDays(7).AddHours(17));
        await ProcessAsync(Monday.AddDays(7));
        var hr = new ExceptionViewer(900, false, false, true);

        var all = await ListAsync(Everything, hr);
        var firstWeek = await ListAsync(Everything with { To = Monday }, hr);
        var secondWeek = await ListAsync(Everything with { From = Monday.AddDays(1) }, hr);
        var withExceptions = await ListAsync(Everything with { OnlyWithExceptions = true }, hr);
        var otherEmployee = await ListAsync(Everything with { EmployeeId = otherId }, hr);
        var taken = await ListAsync(Everything with { PageSize = 1 }, hr);

        Assert.Equal([Monday, Monday.AddDays(7)], all.Select(d => d.AttendanceDate));
        Assert.Equal([Monday], firstWeek.Select(d => d.AttendanceDate));
        Assert.Equal([Monday.AddDays(7)], secondWeek.Select(d => d.AttendanceDate));
        Assert.Equal([Monday.AddDays(7)], withExceptions.Select(d => d.AttendanceDate));
        Assert.Empty(otherEmployee);
        Assert.Single(taken);
        var day = firstWeek.Single();
        Assert.Equal(("E-1", "Ada Lovelace", TimeSpan.FromHours(8), 1, 0), (day.EmployeeNumber, day.EmployeeName, day.TotalWorkingHours, day.ShiftCount, day.ExceptionCount));
    }

    [Fact]
    public async Task TheList_IsPagedOnTheServer_OldestFirst_WithTheTotalOfTheWholeList()
    {
        await ProcessFullDayAsync();
        for (var week = 1; week <= 2; week++)
        {
            AddPunch(NormalizedEventType.CheckOut, Monday.AddDays(7 * week).AddHours(17));
            await ProcessAsync(Monday.AddDays(7 * week));
        }

        var hr = new ExceptionViewer(900, false, false, true);
        var first = await PageAsync(Everything with { PageSize = 2 }, hr);
        var second = await PageAsync(Everything with { Page = 2, PageSize = 2 }, hr);
        var pastTheEnd = await PageAsync(Everything with { Page = 9, PageSize = 2 }, hr);
        var filtered = await PageAsync(Everything with { To = Monday, PageSize = 2 }, hr);

        Assert.Equal((3, 1, 2, 2), (first.Total, first.Page, first.PageSize, first.PageCount));
        Assert.Equal([Monday, Monday.AddDays(7)], first.Items.Select(d => d.AttendanceDate));
        Assert.Equal(2, second.Page);
        Assert.Equal([Monday.AddDays(14)], second.Items.Select(d => d.AttendanceDate));

        // A page past the end is answered with the last page, never an empty one.
        Assert.Equal(2, pastTheEnd.Page);
        Assert.Equal([Monday.AddDays(14)], pastTheEnd.Items.Select(d => d.AttendanceDate));

        // The total follows the filters, not the whole table.
        Assert.Equal((1, 1), (filtered.Total, filtered.PageCount));
    }

    [Fact]
    public async Task TheList_CanBeSortedByEveryColumn_InBothDirections_BeforeItIsCutIntoPages()
    {
        var (managerId, otherId) = AddPeople();
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            AttendanceDaySummary Day(int employee, int dayOffset, int worked, int overtime, int breakMinutes, int exceptions, AttendanceRecordStatus status) => new()
            {
                EmployeeId = employee,
                AttendanceDate = Monday.AddDays(dayOffset),
                TotalWorkingHours = TimeSpan.FromHours(worked),
                TotalOvertimeHours = TimeSpan.FromHours(overtime),
                TotalBreakHours = TimeSpan.FromMinutes(breakMinutes),
                ExceptionCount = exceptions,
                ShiftCount = 1,
                Status = status,
            };

            // Ada Lovelace, Alan Turing, Grace Hopper; (worked h, overtime h, break min, exceptions, status).
            db.AttendanceDaySummaries.AddRange(
                Day(EmployeeId, 0, 8, 0, 60, 0, AttendanceRecordStatus.Approved),
                Day(otherId, 0, 6, 2, 30, 2, AttendanceRecordStatus.Calculated),
                Day(managerId, 0, 6, 0, 120, 1, AttendanceRecordStatus.PendingReview),
                Day(otherId, 1, 7, 1, 60, 0, AttendanceRecordStatus.Approved));
            db.SaveChanges();
        }

        var hr = new ExceptionViewer(900, false, false, true);
        async Task<string[]> Names(RecordSortField sort, bool descending, int page = 1, int pageSize = 100) =>
            (await ListAsync(Everything with { Sort = sort, Descending = descending, Page = page, PageSize = pageSize }, hr))
                .Select(d => $"{d.EmployeeName.Split(' ')[0]}{d.AttendanceDate:dd}").ToArray();

        // Ties are always broken the same way, whatever the direction: by date, then surname (Hopper, Lovelace, Turing).
        Assert.Equal(["Grace05", "Ada05", "Alan05", "Alan06"], await Names(RecordSortField.Employee, false));
        Assert.Equal(["Alan05", "Alan06", "Ada05", "Grace05"], await Names(RecordSortField.Employee, true));
        Assert.Equal(["Grace05", "Ada05", "Alan05", "Alan06"], await Names(RecordSortField.Date, false));
        Assert.Equal(["Alan06", "Grace05", "Ada05", "Alan05"], await Names(RecordSortField.Date, true));
        Assert.Equal(["Grace05", "Alan05", "Alan06", "Ada05"], await Names(RecordSortField.Worked, false));
        Assert.Equal(["Ada05", "Alan06", "Grace05", "Alan05"], await Names(RecordSortField.Worked, true));
        Assert.Equal(["Grace05", "Ada05", "Alan06", "Alan05"], await Names(RecordSortField.Overtime, false));
        Assert.Equal(["Alan05", "Alan06", "Grace05", "Ada05"], await Names(RecordSortField.Overtime, true));
        Assert.Equal(["Alan05", "Ada05", "Alan06", "Grace05"], await Names(RecordSortField.Break, false));
        Assert.Equal(["Grace05", "Ada05", "Alan06", "Alan05"], await Names(RecordSortField.Break, true));
        Assert.Equal(["Ada05", "Alan06", "Grace05", "Alan05"], await Names(RecordSortField.Exceptions, false));
        Assert.Equal(["Alan05", "Grace05", "Ada05", "Alan06"], await Names(RecordSortField.Exceptions, true));
        Assert.Equal(["Ada05", "Alan06", "Alan05", "Grace05"], await Names(RecordSortField.Status, false));
        Assert.Equal(["Grace05", "Alan05", "Ada05", "Alan06"], await Names(RecordSortField.Status, true));

        // The sort applies to the whole list, then it is cut: pages of one follow the same order, once each.
        var pages = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            pages.AddRange(await Names(RecordSortField.Worked, true, page, 1));
        }

        Assert.Equal(await Names(RecordSortField.Worked, true), pages);
    }

    [Fact]
    public async Task ADayWhoseCorrectionWaitsForTheApprover_IsFlagged_ForTheEmployeeTheirManagerAndHr()
    {
        var (managerId, _) = AddPeople();
        AddPunch(NormalizedEventType.CheckOut, Monday.AddHours(17));
        AssignSchedule(StandardDay);
        await ProcessAsync(Monday);
        var viewers = new[]
        {
            new ExceptionViewer(900, false, false, true),
            new ExceptionViewer(EmployeeId, true, false, false),
            new ExceptionViewer(managerId, false, true, false),
        };

        var before = await ListAsync(Everything, viewers[0]);
        AddCorrectionAndReview(managerId);

        Assert.False(Assert.Single(before).HasPendingApproval);
        foreach (var viewer in viewers)
        {
            Assert.True(Assert.Single(await ListAsync(Everything, viewer)).HasPendingApproval);
        }

        // Once the approver decides, the day is no longer waiting.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceApprovals.Single(a => a.Type == AttendanceApprovalType.Correction).Status = AttendanceApprovalStatus.Rejected;
            db.SaveChanges();
        }

        Assert.False(Assert.Single(await ListAsync(Everything, viewers[0])).HasPendingApproval);
    }

    [Fact]
    public async Task AnEmptyList_IsPageOneOfNothing()
    {
        var page = await PageAsync(Everything with { Page = 5 }, new ExceptionViewer(900, false, false, true));

        Assert.Equal((0, 1, 0), (page.Total, page.Page, page.Items.Count));
    }

    [Fact]
    public async Task TheEmployeeOptions_AreTheEmployeesTheViewerMaySee()
    {
        var (managerId, otherId) = AddPeople();

        var hr = await Options(new ExceptionViewer(900, false, false, true));
        var ada = await Options(new ExceptionViewer(EmployeeId, true, false, false));
        var manager = await Options(new ExceptionViewer(managerId, false, true, false));

        Assert.Contains(otherId, hr.Select(o => o.Id));
        Assert.Equal([EmployeeId], ada.Select(o => o.Id));
        Assert.Equal("Ada Lovelace (E-1)", ada.Single().Name);
        Assert.Equal([EmployeeId], manager.Select(o => o.Id));
    }

    [Fact]
    public async Task TheList_FiltersByStatus()
    {
        await ProcessFullDayAsync();
        var hr = new ExceptionViewer(900, false, false, true);
        var status = (await ListAsync(Everything, hr)).Single().Status;

        var same = await ListAsync(Everything with { Status = Enum.Parse<AttendanceRecordStatus>(status) }, hr);
        var other = await ListAsync(Everything with { Status = status == nameof(AttendanceRecordStatus.Rejected) ? AttendanceRecordStatus.Approved : AttendanceRecordStatus.Rejected }, hr);

        Assert.Single(same);
        Assert.Empty(other);
    }

    [Fact]
    public async Task ADay_ShowsItsVersionsExceptionsCorrectionsReviewsAndPunchesInOrder()
    {
        var (managerId, _) = AddPeople();
        AddPunch(NormalizedEventType.CheckOut, Monday.AddHours(17));
        AddPunch(NormalizedEventType.CheckOut, Monday.AddHours(17).AddSeconds(30));
        AssignSchedule(StandardDay);
        await ProcessAsync(Monday);
        var exceptionId = AddCorrectionAndReview(managerId);

        var detail = await GetDayAsync(EmployeeId, Monday, new ExceptionViewer(900, false, false, true));

        Assert.NotNull(detail);
        Assert.Equal(("Ada Lovelace", true), (detail.EmployeeName, detail.Processed));
        var version = Assert.Single(detail.Versions);
        Assert.Equal((1, true), (version.Version, version.IsCurrent));
        var review = Assert.Single(version.Reviews);
        Assert.Equal(("Rejected", "Grace Hopper", "Manager", "Not enough evidence."), (review.Outcome, review.DecidedByName, review.DecidedByRole, review.Reason));
        var exception = detail.Exceptions.Single(e => e.Id == exceptionId);
        Assert.Equal(("MissingPunch", "CheckIn", "Open"), (exception.ExceptionType, exception.MissingEventType, exception.Status));
        var correction = Assert.Single(detail.Corrections);
        Assert.Equal(("CheckIn", "Ada Lovelace", "Pending", "Manager"), (correction.CorrectedEventType, correction.RequestedByName, correction.Status, correction.ApprovalWorkflow));
        Assert.Equal(["Front door", "Front door"], detail.Punches.Select(p => p.DeviceName));
        Assert.Equal([Monday.AddHours(17), Monday.AddHours(17).AddSeconds(30)], detail.Punches.Select(p => p.LocalTime));
    }

    [Fact]
    public async Task ADayNeverProcessed_IsShownEmpty_AndOneOutOfScopeOrUnknownIsNull()
    {
        var (_, otherId) = AddPeople();

        var empty = await GetDayAsync(EmployeeId, Monday, new ExceptionViewer(900, false, false, true));

        Assert.False(empty!.Processed);
        Assert.Empty(empty.Versions);
        Assert.Null(await GetDayAsync(EmployeeId, Monday, new ExceptionViewer(otherId, true, true, false)));
        Assert.Null(await GetDayAsync(9999, Monday, new ExceptionViewer(900, false, false, true)));
        Assert.NotNull(await GetDayAsync(EmployeeId, Monday, new ExceptionViewer(EmployeeId, true, false, false)));
    }

    [Fact]
    public async Task ReadingADaysPunches_IsAudited_ButAnOutOfScopeAttemptReadsNothing()
    {
        var (_, otherId) = AddPeople();
        await GetDayAsync(EmployeeId, Monday, new ExceptionViewer(900, false, false, true));
        await GetDayAsync(EmployeeId, Monday, new ExceptionViewer(otherId, true, false, false));

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var entry = Assert.Single(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead).ToList());
        Assert.Contains($"employee {EmployeeId} on 2026-10-05", entry.Details);
    }

    [Fact]
    public async Task Reprocessing_KeepsAnUnchangedDay_AndWritesANewVersionAfterAPolicyChange()
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.AddHours(8).AddMinutes(7));
        AddPunch(NormalizedEventType.CheckOut, Monday.AddHours(17));
        AssignSchedule(StandardDay);
        await ProcessAsync(Monday);

        var unchanged = await ReprocessAsync(EmployeeId, Monday);
        AddDefaultPolicy(p => p.RoundingRuleMinutes = 15);
        var changed = await ReprocessAsync(EmployeeId, Monday);

        Assert.Equal((0, 1, 0), (unchanged!.RecordsWritten, unchanged.RecordsUnchanged, unchanged.RecordsRetired));
        Assert.Equal((1, 0), (changed!.RecordsWritten, changed.RecordsUnchanged));
        var detail = await GetDayAsync(EmployeeId, Monday, new ExceptionViewer(900, false, false, true));
        Assert.Equal([2, 1], detail!.Versions.Select(v => v.Version));
        Assert.Equal([true, false], detail.Versions.Select(v => v.IsCurrent));
        Assert.Equal(detail.Versions[0].Id, detail.Versions[1].SupersededByRecordId);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(2, db.AttendanceAuditLogs.Count(a => a.Action == AttendanceAuditActions.DayReprocessed));
    }

    [Fact]
    public async Task ReprocessingAnUnknownEmployee_ReturnsNull_AndAuditsNothing()
    {
        Assert.Null(await ReprocessAsync(9999, Monday));

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceAuditLogs);
    }

    private (int ManagerId, int OtherId) AddPeople()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var manager = new Profile { EmployeeNumber = "M-1", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
        var other = new Profile { EmployeeNumber = "E-2", FirstName = "Alan", LastName = "Turing", Title = "Alan Turing" };
        db.AddRange(manager, other);
        db.SaveChanges();
        db.Profiles.Single(p => p.Id == EmployeeId).ManagerId = manager.Id;
        db.SaveChanges();
        return (manager.Id, other.Id);
    }

    private async Task ProcessFullDayAsync()
    {
        AddPunch(NormalizedEventType.CheckIn, Monday.AddHours(8));
        AddPunch(NormalizedEventType.BreakOut, Monday.AddHours(12));
        AddPunch(NormalizedEventType.BreakIn, Monday.AddHours(13));
        AddPunch(NormalizedEventType.CheckOut, Monday.AddHours(17));
        AssignSchedule(StandardDay);
        await ProcessAsync(Monday);
    }

    private async Task ProcessOtherDayAsync(int otherId)
    {
        // A day summary for someone else: the list is about scope, so no engine run is needed.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceDaySummaries.Add(new AttendanceDaySummary { EmployeeId = otherId, AttendanceDate = Monday, ShiftCount = 1 });
        await db.SaveChangesAsync();
    }

    private int AddCorrectionAndReview(int managerId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exception = db.AttendanceExceptions.Single(e => e.MissingEventType == NormalizedEventType.CheckIn);
        var record = db.AttendanceRecords.Single();
        db.AttendanceCorrections.Add(new AttendanceCorrection
        {
            AttendanceApproval = new AttendanceApproval
            {
                Type = AttendanceApprovalType.Correction,
                EmployeeId = EmployeeId,
                AttendanceDate = Monday,
                RequestedByEmployeeId = EmployeeId,
            },
            AttendanceExceptionId = exception.Id,
            EmployeeId = EmployeeId,
            AttendanceDate = Monday,
            CorrectedEventType = NormalizedEventType.CheckIn,
            ProposedValue = Monday.AddHours(8),
            TimeZoneId = "UTC",
            Justification = "Forgot to punch.",
            RequestedByEmployeeId = EmployeeId,
        });
        db.AttendanceApprovals.Add(new AttendanceApproval
        {
            Type = AttendanceApprovalType.Review,
            AttendanceRecordId = record.Id,
            EmployeeId = EmployeeId,
            AttendanceDate = Monday,
            Status = AttendanceApprovalStatus.Rejected,
            Workflow = ApprovalWorkflow.ManagerOrHr,
            ManagerDecision = AttendanceCorrectionDecision.Rejected,
            ManagerDecidedById = managerId,
            ManagerDecidedAt = Monday,
            Reason = "Not enough evidence.",
        });
        db.SaveChanges();
        return exception.Id;
    }

    private async Task<IReadOnlyList<robeem.Contracts.Attendance.RecordDayListItem>> ListAsync(RecordFilter filter, ExceptionViewer viewer) =>
        (await PageAsync(filter, viewer)).Items;

    private async Task<IReadOnlyList<robeem.Contracts.Attendance.EmployeeOption>> Options(ExceptionViewer viewer)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ListEmployeeOptionsAsync(viewer, CancellationToken.None);
    }

    private async Task<robeem.Contracts.Common.PagedResult<robeem.Contracts.Attendance.RecordDayListItem>> PageAsync(RecordFilter filter, ExceptionViewer viewer)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ListDaysAsync(filter, viewer, CancellationToken.None);
    }

    private async Task<robeem.Contracts.Attendance.RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, ExceptionViewer viewer)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).GetDayAsync(employeeId, date, viewer, CancellationToken.None);
    }

    private async Task<robeem.Contracts.Attendance.ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ReprocessAsync(employeeId, date, CancellationToken.None);
    }

    private AttendanceRecordService Service(RobeemDbContext db) => new(db, CreateEngine(db), CreateAudit(db));
}
