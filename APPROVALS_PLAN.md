# One approval table and one Approvals screen (corrections + reviews)

Work through the phases below, each ending green (`dotnet build` 0 warnings, `dotnet test`, `dotnet format --verify-no-changes`).

## Context

Approvers have two screens and two workflows: **Corrections** (decide an employee's proposed edits to a day) and **Reviews**
(approve or reject a record the engine left `PendingReview`). Decided with the product owner:

- **One table**: a single `AttendanceApproval` row is the workflow item for both, and the one source of truth for its status and
  decisions.
- **One mixed screen**, *Approvals*, replacing the Corrections and Reviews Web screens.
- One piece of workflow code (steps, who may decide, what completes it) used for both types.

Before this change: corrections lived in `Attendance.AttendanceCorrection` (one row per edit, with the approval state repeated on
every row of a request via `RequestId`), review decisions in `Attendance.AttendanceReviewDecision` (insert-only; payroll reads it),
and the review queue was *derived* from `AttendanceRecord` current rows with `Status == PendingReview`.

## The model

New entity `AttendanceApproval : Entity, ITenantScoped, IWorkflowEntity`, table `Attendance.AttendanceApproval`
(`IWorkflowEntity` moves here from `AttendanceCorrection`).

| Field | Meaning |
|---|---|
| `Type` | `Correction` or `Review` (`AttendanceApprovalType`) |
| `EmployeeId` / `Employee`, `AttendanceDate` | whose day |
| `Status` | `Pending`, `Approved`, `Rejected`, `Applied`, `Superseded` (`AttendanceApprovalStatus`). Correction: Pending -> Applied or Rejected. Review: Pending -> Approved, Rejected, or Superseded (the record was recalculated or no longer needs review; not a human decision) |
| `Workflow` | `ApprovalWorkflow`: `Manager`, `HR`, `Both` (from the policy, copied at creation), `ManagerOrHr` (review: one decision by the employee's manager or HR) |
| `ManagerDecision/By/At`, `HrDecision/By/At` | the two steps. A review decided by a manager fills the manager slot, by HR the HR slot |
| `Reason` | reviewer's reason (required to reject a review) |
| `RequestedByEmployeeId?`, `RequestedAt` | correction: the employee; review: null / when the engine queued it |
| `AppliedAt` | correction applied |
| `AttendanceRecordId?` / `AttendanceRecord`, `OvertimeMinutes` | review only: the record **version** it covers and the overtime it covered |

Changes to existing entities:

- `AttendanceCorrection` keeps the edit payload (kind, punch, times, justification, requested by/at). It **loses** `RequestId`,
  `ApprovalWorkflow`, `Status`, `Manager*`, `Hr*`, `AppliedAt`, and gains `AttendanceApprovalId` (required FK, nav
  `AttendanceApproval`). A request is the approval; its edits are the rows that point to it.
- `AttendanceReviewDecision` is **removed** (its data becomes `Review` approvals).
- `AttendanceRecord.Status` stays (payroll and the engine read it); the approval is the decision record, the record mirrors it.
- Indexes: `(TenantId, AttendanceRecordId)` unique where not null (one review approval per record version);
  `(TenantId, Status, Type, AttendanceDate)` for the queue; `AttendanceCorrection (TenantId, AttendanceApprovalId)`.
- No cascade deletes (CLAUDE.md §8).

## Phases

### Phase 1 - Schema and data move (Domain, Infrastructure)

- New entity, enums, `IEntityTypeConfiguration`, `DbSet` on `IRobeemDbContext` and `RobeemDbContext`; edit `AttendanceCorrection` and its
  configuration; delete `AttendanceReviewDecision` and its configuration.
- Migration `UnifyApprovals`, hand-written data step like `AddConnections`/`MoveCatalogToPrime`:
  1. create `AttendanceApproval`;
  2. one `Correction` approval per distinct `RequestId`: status/workflow/decisions/applied copied from the request's rows,
     `RequestedAt` = earliest; set each row's `AttendanceApprovalId`;
  3. one `Review` approval per `AttendanceReviewDecision` (Outcome -> Approved/Rejected, `DecidedByRole` -> manager or HR slot, reason,
     overtime, record id, decided-at);
  4. one `Pending` `Review` approval per current record with `Status = PendingReview` that has none;
  5. drop the moved columns and `AttendanceReviewDecision`.
  `Down` is not supported (like `MoveCatalogToPrime`). **Back up the tenant databases before running it.**
- A case in the opt-in `PostgresMigrationTests` builds the previous schema with corrections (single and multi-edit, each status)
  and review decisions, applies the migration and asserts the approvals.

### Phase 2 - One workflow engine (Application)

- `ApprovalWorkflow` rules component holding what was in `AttendanceCorrectionService` (`RoleProblem`, stand-in for an unmanaged
  employee, "manager first then HR", `complete`) and `AttendanceReviewService` (not your own, manager of the employee or HR,
  rejection needs a reason).
- `IAttendanceApprovalService` / `AttendanceApprovalService`:
  - `ListPendingAsync(...)` -> `PagedResult<ApprovalListItem>`, one query on `AttendanceApproval` (Pending, step visible to the
    approver, review rows excluded while an exception of that employee/date/shift is open and when it is the approver's own),
    filters employee / dates / type / review reason, stable order, total with the same filters, last page past the end, size
    clamped 1..200 (CLAUDE.md §2.9).
  - `DecideAsync(approvalId, ...)`: dispatches on `Type`. Correction: apply the edits, close exceptions, recalculate, one
    transaction. Review: set the record status, recalculate, roll back if the day changed under the reviewer. Both audited.
- The correction and day-correction services create the `AttendanceApproval` with the edit rows pointing to it.
  `DecideCorrectionAsync`/`ListPendingAsync` and the review service's equivalents stay as thin wrappers so the old Api endpoints keep
  working.
- `AttendanceProcessingEngine` keeps the approvals in step with the records: a record written as `PendingReview` gets a `Pending`
  `Review` approval; a retired version, or one the engine moves `PendingReview -> Approved`, has its `Pending` approval set to
  `Superseded`. Corrections the engine reads (`Applied`) are read through their approval.
- Payroll reads `AttendanceApproval` (`Type == Review && Status == Approved`) instead of `AttendanceReviewDecisions`. The gate itself
  does not change (§1.8).
- Self-heal in the nightly reconciliation: every current `PendingReview` record has a `Pending` review approval and no `Pending`
  review approval points at a non-current or non-pending record.

### Phase 3 - Api and Web

- Contracts: `ApprovalType`, `ApprovalFilter`, `ApprovalListItem` (with `AwaitingStep`), `DecideApprovalRequest`, `ApprovalDecisionResponse`.
- Api: `GET api/attendance/approvals/pending` and `POST api/attendance/approvals/{id}/decision`, roles `Approvers`. The old
  `corrections/...` and `reviews/...` list and decision endpoints stay as wrappers.
- Web: `ApprovalsController`, `ApprovalsApiClient`, view and view model; the Corrections and Reviews controllers, views and view models
  are removed; one *Approvals* menu item. The Manager/HR tabs go away: each row knows its step.

### Phase 4 - Tests and docs

- Update the existing correction, review, record, payroll, Api and Web tests; add tests for approval paging, visibility, deciding both
  types, engine sync, reconciliation heal and the Web pager.
- Docs: `CLAUDE.md` (roadmap bullet, §2.7 marker, §4 table), `TEST_COVERAGE.md`.

## Risks

- The migration moves payroll-relevant data and cannot be undone: back up, run the Postgres migration test first.
- The engine now has one more job (keeping review approvals in step); the reconciliation check catches drift.
- Large change (both workflows, payroll, engine). Phases 1-2 change no screen.

## Verification

- `dotnet build robeem.attendance.slnx` (0 warnings), `dotnet test robeem.attendance.slnx`, `dotnet format robeem.attendance.slnx --verify-no-changes`.
- `ROBEEM_TEST_POSTGRES` set: `dotnet test --filter FullyQualifiedName~PostgresMigrationTests`.
- Run Api + Web on a copy of the dev data: the Approvals screen shows the same pending items as before the migration, approving one
  of each removes it and returns to the same filters and page, payroll's approved list is unchanged, and a late punch that
  recalculates a day supersedes its pending review and queues a new one.
