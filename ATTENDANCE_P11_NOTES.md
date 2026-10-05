# Attendance P11 — payroll extraction contract: notes

## What Prompt 11 added

- **Contract:** `IPayrollAttendanceExtract.GetApprovedRecordsAsync(payPeriodStart, payPeriodEnd, ct)` in `src/robeem.Application/Attendance`, implemented by `PayrollAttendanceExtract` (scoped). The tenant is the current one; there is no tenant parameter. It returns `PayrollAttendanceDay`s (employee, date, the day summary's totals, and the current records as `PayrollAttendanceShift`s), ordered by date then employee number. These are Application records, not entities: payroll gets no entity from Attendance.
- **Entity:** `PayPeriod` (`Entity`, `ITenantScoped`; `StartDate`, `EndDate`, `IsClosed`), schema `Attendance`, unique on `(TenantId, StartDate, EndDate)`. Migration `AddPayPeriods`. As the prompt says, it is a stand-in for a Payroll module that does not exist: a real one should take it over.
- **Architecture tests:** `tests/robeem.Tests/Architecture/AttendanceDataAccessArchitectureTests.cs`, using **NetArchTest.Rules 1.3.2**. This is new tooling (a new test-only package), not something that was already in place. It asserts:
  - Web and Contracts do not use `RawAttendanceTransaction` / `NormalizedAttendanceTransaction`, and do not reference Domain, Application or Infrastructure at all;
  - inside Application, only `robeem.Application.Attendance` and `robeem.Application.Persistence` (`IRobeemDbContext` exposes the DbSets) use them, so HR and the rest do not;
  - the payroll extract does not use them;
  - a sanity check that the rules can see a dependency (`RobeemDbContext` has them).

  Api is allowed by the prompt and not checked. **The tests were verified to catch violations:** I temporarily added `_db.NormalizedAttendanceTransactions` inside the async method of the extract, and `_db.RawAttendanceTransactions` in `EmployeeService`. Both rules failed, naming the offending class (the async state machine is covered), and I reverted both.

## Rules later prompts must keep

- **Anything outside Attendance reads attendance through `IPayrollAttendanceExtract` only.** Never raw, never normalized (CLAUDE.md §1.8).
- **A day is all or nothing.** It is returned only when its summary and *every* current record are `Approved`; one pending shift keeps the whole day out, so payroll never pays part of a day.
- Only `IsCurrent` records count; retired versions are history.

## The gate, and what I had to decide

A day is returned only when all of these hold:

| Rule | Implementation |
|---|---|
| Approved | the day summary and each current record are `Approved` (a day with no records is `Calculated`, so it is out). |
| No open exception | no `AttendanceException` of that employee **and date** (any shift) with a status other than `Closed`: "Open or PendingApproval" in the prompt is `Open` or `PendingCorrection` in this solution. |
| Overtime pre-approval | a record with overtime under a policy with `RequiresOvertimePreApproval` (resolved through `IAttendancePolicyResolver`, as P7 does) needs an `AttendanceCorrection` of that employee and date in status `Approved` or `Applied`. |
| Open pay period | no *closed* `PayPeriod` contains the date. |

- **The date range is inclusive at both ends**, dates only (the time of day is ignored). An end before the start throws `ArgumentException`.
- **The signature takes dates, not a pay period id**, so "the period is not closed" means: no closed `PayPeriod` contains the day. Days with no `PayPeriod` rows at all are returned. Nothing creates or closes a `PayPeriod` yet: there is no service or endpoint for it, only the entity, as the prompt asked. Closing a period does not check that it does not overlap another one.

## Please look at: the overtime rule is mostly a safety net

- **No path approves a `PendingReview` record yet.** The engine sets overtime under a pre-approval policy to `PendingReview`, and nothing (no review screen, no endpoint) moves a record to `Approved` afterwards. So the overtime rule is, today, redundant with "Approved".
- **A correction can only supply a missing punch (P9).** There is no "overtime pre-approval" correction type. I read "covering it" as *a correction of that employee and date, Approved or Applied*, which is the closest thing there is. That means a missing-punch correction on a day would also satisfy it. If the study defines an overtime approval as its own thing, this rule must be tightened when that exists; **the P12 review/approval work is the place**.
- The tests therefore approve the record by hand to exercise the rule, and say so.

## Tests

`PayrollAttendanceExtractTests` (10 cases) and the 5 architecture tests. The four the prompt asks for:

- an Approved day with no exceptions is returned (`ApprovedDayWithNoExceptions_IsReturned`);
- an Open (and a PendingCorrection) exception of the same employee and date excludes it, a Closed one does not (`ApprovedDay_WithAnExceptionStill…`);
- overtime needing pre-approval is excluded until the correction exists, with a merely `Pending` correction not enough (`OvertimeNeedingPreApproval_IsExcluded_UntilTheCorrectionExists`);
- a day in a closed pay period is excluded, one in an open period is not (`DayInAClosedPayPeriod…`).

The rest cover: overtime with no pre-approval policy, the inclusive range, the current version winning over a retired one, a reversed range, and tenant isolation.
