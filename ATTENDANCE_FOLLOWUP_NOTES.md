# Follow-up after P15: sync batches, approver scope, review decisions

Three items left open by earlier prompts, done together. The owner's decisions are marked **(decided)**; the rest I chose and flag for review.

## A. A batch for every sync, empty ones pruned (decided)
- A successful sync or reconciliation that delivers nothing now leaves a 0-record `Succeeded` batch, so the health report's sync success rate is no longer biased low (`ATTENDANCE_P13_NOTES.md`).
- `IIntegrationSyncService.PruneEmptyBatchesAsync`, called nightly by `ReconciliationJob` for each tenant, deletes empty successful sync and reconciliation batches older than `Attendance:Sync:EmptySyncRetentionDays` (default **7**, my choice; 0 keeps all). Failed batches, batches with raw rows and uploads are never deleted. Raw rows are untouched (invariant 2).
- The health rate is meaningful only for windows within the retention.

## B. Approver scope: an optional manager on the employee (decided)
- `Profile.ManagerId` / `Manager` (migration `AddEmployeeManager`), set on the HR employee screen. Validation: the manager exists, is active, is not the employee, and no reporting loop at any depth.
- A manager sees and decides **only their direct reports'** corrections; the queue is asked for the authenticated user, never for whoever the caller names.
- **An employee with no manager (decided: HR only), or an inactive one, is decided by HR.** HR sees their manager-step corrections and decides them in the manager's place (`isHrDecision` on a correction still awaiting its manager step is recorded as that step). Under `Both`, HR then decides the HR step too: two decisions, possibly the same person. A managed employee's manager step stays the manager's; HR cannot take it.
- The inactive-manager rule is my addition, so that deactivating a manager cannot leave corrections undecidable.

## C. Review decisions: how a PendingReview record gets approved (design chosen by me; the question was not answered)
- `AttendanceReviewDecision` (migration `AddAttendanceReviewDecisions`): an additive row per record **version**: outcome, who, in which role (Manager/HR), when, reason, and the overtime it covered. Unique per record.
- `IAttendanceReviewService` and `api/attendance/reviews` (`GET pending`, `POST {recordId}/decision`, roles Manager/HRAdministrator). It covers every PendingReview reason (overtime, unscheduled work, incomplete day), not overtime only. A manager reviews direct reports, HR anyone; nobody reviews their own record; a rejection needs a reason; **a record with an open exception can't be reviewed** (settle the exception first, CLAUDE.md §1.5).
- Approving sets the record (and, through the engine, the day summary) to Approved; rejecting, to Rejected. Decision, status, audit entry and recalculation are one transaction. **If the recalculation writes a new version** (a punch arrived while the reviewer was looking), nothing is kept and the reviewer is asked to review again. A later change to the numbers makes a new version that needs a new decision: an approval of 1:30 never covers 2:30.
- **Payroll** now requires, for a record with overtime under a pre-approval policy, an *approving decision of that record version*. The old stand-in (any Approved/Applied correction that day) is gone, as is its "a missing-punch correction counts" loophole.
- Audit action `ReviewDecided`.

## Please look at
- Review is **one step** by manager or HR. The correction policy's `Manager/HR/Both` workflow is not applied to it.
- A rejection is final for that version; there is no override. The numbers must change (or HR corrects the data) to get a new version to review.
- Approving an incomplete day that has no exception yet is allowed (before the grace ends); if the exception is raised afterwards the engine takes the approval back.
- HR's own queue never contains HR's own records; HR cannot approve their own overtime.
- **Not done:** Web screens for review and for the manager-based queues (the Web has no Attendance screens), and API-level tests for the new reviews controller beyond the "every action names its roles" check.
