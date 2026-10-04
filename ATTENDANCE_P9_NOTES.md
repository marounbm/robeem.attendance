# Attendance P9 — corrections and approval workflow: notes

## What Prompt 9 added

- **Entity:** `AttendanceCorrection` (`Entity`, `ITenantScoped`, **`IWorkflowEntity`**), with `AttendanceCorrectionStatus` (`Pending`, `Approved`, `Rejected`, `Applied`) and `AttendanceCorrectionDecision` (`Approved`, `Rejected`). `DbSet`, EF configuration, migration `AddAttendanceCorrections`.
- **Service:** `IAttendanceCorrectionService` / `AttendanceCorrectionService` with `ProposeCorrectionAsync`, `DecideCorrectionAsync` (the two methods of the prompt) and `ListPendingAsync`.
- **Engine:** `ProcessEmployeeDayAsync` now reads the employee's `Applied` corrections as punches with source `Corrected`. `AttendanceRecord.SourceCorrectionIds` records which ones a calculation used (same migration).
- **Api:** `AttendanceCorrectionsController`, route `api/attendance/corrections`:
  - `POST` — propose (`ProposeCorrectionRequest`); `201` with `CreatedResponse`.
  - `GET pending?awaiting=Manager|HR` — the approver's queue (`CorrectionListItem`).
  - `POST {id}/decision` — decide (`DecideCorrectionRequest`); returns `CorrectionDecisionResponse`.
  - Errors are `400` ValidationProblemDetails keyed by request property names; an unknown id is `404`.
- **Contracts:** the five DTOs above in `src/robeem.Contracts/Attendance`, plus `CorrectionApprover`.
- `IRobeemDbContext` exposes `Database` (the facade), so the service can wrap several saves in a transaction.

## No authentication yet: the TODOs for P12

There is no authentication or role system (CLAUDE.md §2.8, DISCOVERY_NOTES.md), so I did not invent one. The requester, the decider and the decider's role all come from the request, and any caller can act as anyone. `TODO(P12)` comments mark each place:

- `ProposeCorrectionRequest.RequestedByEmployeeId`: must become the authenticated user.
- `DecideCorrectionRequest.DecidedByUserId` and `IsHrDecision`: the user from the token; the role from their permissions, not from the caller.
- `AttendanceCorrectionService.ListPendingAsync`: needs the approver's scope (team or department). There is no manager or organization structure to scope by, so **the list is every pending correction awaiting that role**.
- The controller's summary comment says the endpoints must not be exposed outside Development until then. In production no request reaches tenant data anyway: tenant resolution needs a `tenant_id` claim that nothing issues yet.

"User" in `decidedByUserId` is a `Profile` id, the entity the solution uses for people.

## The study was not available, so these were reconstructed

As before, the study is not in the repository (§11.1, §11.2, §11.3, Appendix B Scenario 3 unread). Please compare:

- **Fields of `AttendanceCorrection` (§11.2):** `AttendanceExceptionId`, `EmployeeId` (the exception's employee, copied for queries), `AttendanceDate`, `CorrectedEventType`, `ProposedValue` (local wall-clock time), `TimeZoneId`, `Justification`, `RequestedByEmployeeId`/`RequestedByEmployee`, `RequestedAt`, `ApprovalWorkflow`, `Status`, `ManagerDecision`/`ManagerDecidedById`/`ManagerDecidedAt`, `HrDecision`/`HrDecidedById`/`HrDecidedAt`, `AppliedAt`. Decider and requester are `Profile`s; the navigation names follow the FK-minus-`Id` rule (`ManagerDecidedBy`, `HrDecidedBy`).
- **Scenario 3:** the same inputs as in P8 (08:00 in, 13:00 break-in, 17:00 out; break-out missing), corrected to 12:00 as the prompt says. Asserted: the day is recalculated into version 2 with 8:00 worked and a 1:00 break (the standard day's numbers), the break-out has source `Corrected`, the record is `Approved`, version 1 is kept, the exception is `Closed` with `ResolutionMethod.Correction`. If the study's Scenario 3 has other numbers, only the test data changes.

## Rules I had to decide

- **The approval workflow is copied from the policy when the correction is proposed** (`ApprovalWorkflow`), not read at decision time. A policy change then cannot alter a correction already under review, and the pending list needs no policy lookup.
- **Who decides:**

  | Policy | Accepted decisions | Applied when |
  |---|---|---|
  | `Manager` | manager only | the manager approves |
  | `HR` | HR only | HR approves |
  | `Both` | the manager first, then HR | HR approves, after the manager did |

  A decision by the wrong role, HR before the manager under `Both`, or a second manager decision is refused with an error on `IsHrDecision`. **A rejection by either approver rejects the correction** at once. Under `Both` with only the manager's approval in, the correction stays `Pending` and the exception stays `PendingCorrection`; the day is not recalculated.
- **Only the exception's own employee can propose** (`RequestedByEmployeeId` must equal the exception's employee), as the prompt says: "an employee submits a correction against their own open exception". Managers can't propose for them yet; that is a decision for P12's roles.
- **The decider can't be the proposer.** Not in the prompt: a basic separation of duties that costs nothing now. Remove it if the study allows self-approval.
- **Only a `MissingPunch` exception can be corrected.** A correction supplies a punch, and adding a punch can't fix a punch that is out of order. An `InvalidSequence` exception therefore has **no resolution path yet**: it stays `Open` until its cause changes (its punches change), when reprocessing closes it as auto-resolved. A way to resolve it needs a decision: a correction that *excludes* the offending punch (additive, as an excluded-by-correction mark, never a delete) is the likely design, but it is a different kind of correction from the one the prompt specifies.
- **One correction at a time per exception:** only an `Open` exception accepts a proposal, and a unique index (filtered to `Pending`/`Approved` corrections) backs it up against two proposals racing. A rejected correction frees the exception.
- **The proposed time** must be on the shift's date or the day after (an overnight shift ends the next day), and not in the future.
- **Time zone of the proposed time.** The employee gives a local time. It is read in the zone of the employee's punch nearest to it (the device the schedule is compared with), else the server's zone, and that zone is stored on the correction so the instant never shifts. A time that doesn't exist in the zone (a daylight-saving gap) is refused.
- **Apply, close and recalculate are one transaction.** The correction goes `Applied` and the exception `Closed` first, and the engine is run after; otherwise the day would be calculated with the exception still not closed and could never come out `Approved`. If the recalculation fails, everything rolls back. `Approved` is therefore only a transient status; it is never stored by the service (kept in the enum for the study's state list).
- **Raw and normalized punches are untouched.** Invariant 7: the correction is a new row; the engine adds its punch to the day's list (source `Corrected`, no normalized transaction id).

## Changed beyond the prompt

- `AttendancePunch.NormalizedTransactionId` is nullable, and the punch has a `CorrectionId`. The exception detector reports an out-of-order *corrected* punch with no `RelatedNormalizedTransactionId` (only a missing-punch exception can be corrected, so a corrected punch can still, by its time, create an out-of-order sequence; that case is raised, just without a related punch).
- The no-show check in the detector looks at typed punches as well as normalized ids, so a day with only a corrected punch isn't taken for a no-show.

## Not done

- **No Web screens** and no list of exceptions: an employee has no endpoint to find the exception id to correct. `GET api/attendance/exceptions` is a small addition if you want it with P9 rather than later.
- **No controller tests.** The service is tested end to end (11 tests); the controller is a thin mapping like `EmployeesController`, which has none either.
- **Not run against PostgreSQL.** I checked the SQL with `dotnet ef migrations script` (`ALTER TABLE ... "SourceCorrectionIds" text`, the table, and the filtered unique index) but did not start the Api.
- **Audit logging** of decisions (Prompt 12's "every AttendanceCorrection decision"): the decisions are stored on the row with who and when; the audit log itself is P12.

## Verification

- `dotnet build robeem.attendance.slnx`: 0 warnings, 0 errors.
- `dotnet test robeem.attendance.slnx`: 129 pass (118 before, plus 11 in `AttendanceCorrectionServiceTests`: Scenario 3 end to end; the applied correction surviving reprocessing; propose, reject, reopen and propose again; `Both` with only the manager's approval staying `Pending`, then HR applying; HR before the manager and the manager's rejection under `Both`; HR-only; manager-only refusing HR; the pending list; the proposal refusals; an invalid sequence not correctable; the decision refusals).
- `dotnet format robeem.attendance.slnx --verify-no-changes`: passes.
