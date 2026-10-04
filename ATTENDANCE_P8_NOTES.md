# Attendance P8 — exception detection: notes

## What Prompt 8 added

- **Entity:** `AttendanceException` (`Entity`, `ITenantScoped`, not `ISeedData`, not `IWorkflowEntity`), with the enums `AttendanceExceptionType` (`MissingPunch`, `InvalidSequence`), `AttendanceExceptionStatus` (`Open`, `PendingCorrection`, `Closed`) and `AttendanceExceptionResolutionMethod` (`AutoResolved`, `Correction`). `DbSet`, EF configuration, migration `AddAttendanceExceptions`.
- **Policy column:** `AttendancePolicy.MissingPunchGraceMinutes`, default 120. The migration backfills existing policies with 120 (the generated `defaultValue: 0` was edited), so they match the entity default.
- **Detection:** `AttendanceExceptionDetector` (pure; the clock is a parameter). `AttendanceProcessingEngine` calls it after the pairing step and reconciles the result with the stored exceptions.
- **Clock:** the engine now takes a `TimeProvider` (registered with `TryAddSingleton(TimeProvider.System)`), and reads local wall-clock time with `GetLocalNow()`, like the rest of the solution's local times.
- **Results:** `DayProcessingResult` has two more members, `ExceptionsRaised` and `ExceptionsResolved` (default 0, so existing callers compile). `AttendanceDaySummary.ExceptionCount` is now set: the exceptions of the day that are not closed.

## The study was not available, so these were reconstructed

As in P7, the study is not in the repository. I could not read §9.2, §9.3, §11.3, §14.1 or Appendix B. Please compare these choices with it:

- **Fields of `AttendanceException` (§9.3).** `EmployeeId`/`Employee`, `AttendanceDate`, `TimeScheduleShiftId`/`TimeScheduleShift` (null for unscheduled work), `ExceptionType`, `MissingEventType`, `RelatedNormalizedTransactionId`/`RelatedNormalizedTransaction` (the out-of-order punch), `Status`, `Description`, `DetectedAt`, `ResolvedAt`, `ResolutionMethod`. There is **no `AttendanceRecordId`**: records are replaced on every recalculation, so a link to one version would go stale. P9 reaches the record by employee + date + shift.
- **Scenario 3 (missing Break Out).** Inputs: 08:00 check-in, 13:00 break-in, 17:00 check-out on the 08:00-17:00 shift with a 12:00-13:00 break. They match what Prompt 9 quotes (the break-out is later corrected to 12:00). Asserted: one open `MissingPunch`/`BreakOut`, the record has no break-out, 9:00 worked and no break deducted (nothing is invented), `PendingReview`, `ExceptionCount` 1. If the study's scenario has different times or numbers, only the test data changes.
- **Grace window default (120 minutes).** Study §9.2 says "configurable"; the number is mine. It lives on the policy, so it can differ per schedule.

## Rules I had to decide

The prompt lists the same situation as both a missing punch and an invalid sequence ("a BreakIn with no preceding BreakOut", "a CheckOut with no preceding CheckIn"). I split them like this:

| Situation | Raised as |
|---|---|
| Check-out with no check-in anywhere in the shift | `MissingPunch` / `CheckIn` |
| Check-out *before* a check-in that exists later | `InvalidSequence` on the check-out (§14.1 "CheckOut before CheckIn") |
| Break-in with no break-out, shift has `BreakTime=true` | `MissingPunch` / `BreakOut` |
| Break-in with no break-out, shift has `BreakTime=false` | `InvalidSequence` on the break-in (§14.1) |
| Break-out with no break-in, `BreakTime=true` | `MissingPunch` / `BreakIn` |
| Break-out with no break-in, `BreakTime=false` | `InvalidSequence` on the break-out. A break is unexpected there, and the day would otherwise be incomplete with nothing for a person to resolve |
| Other repeats and strays (second check-in with no check-out between, check-in during a break, break punch before the check-in or after the check-out, ...) | `InvalidSequence` on the stray punch |

Timing: a missing punch is raised **at once** when a later punch proves it was due (a check-out with no check-in, a break-in with no break-out, a check-out while still on a break). Otherwise it might simply not have happened yet, so it waits until the shift's end plus the grace: a missing check-out, a dangling break-out that is the last punch, and the no-show below. For work outside any shift, "the end" is midnight of the attendance date.

**Beyond the five listed cases** (Invariant 5: a missing punch always produces an exception; otherwise a record is `PendingReview` with nothing for a person to act on). Both are easy to remove if the study says otherwise:

- **No punch at all on a scheduled shift** raises `MissingPunch` for `CheckIn` and `CheckOut`, after the grace. A day off or an employee with no schedule never does (see the `DayOff` guard below).
- **A shift with a break, worked without any break punch** raises `MissingPunch` for `BreakOut` and `BreakIn`, as soon as there is a check-out, or after the grace.

There is no leave or holiday module yet, so a leave day on a scheduled shift is raised as a no-show. That is for a person to close, not something to guess.

### The duplicate window (§14.1)

Two **consecutive punches of the same type** inside `AttendancePolicy.DuplicateTimeWindowSeconds` count as one punch, and neither raises an exception. The prompt asked for this on check-ins; I applied it to all four types. It is done in `ShiftAttendanceCalculator` (`WithoutRepeats`), so the calculation and the detector see the same list, and it keeps the first of the pair. Normalization buckets by time, so two near-identical punches can land in different buckets and both be `Unique`; before this, the second made the record incomplete. Only a repeat *outside* the window is an `InvalidSequence`.

### The `DayOff` guard (item 4)

There is no special case: the detector runs once per scheduled shift (and once for unscheduled punches), so a day off has nothing to analyze. `IScheduleResolutionService` returns no shifts for `DayOff` and `NoScheduleConfigured`, and the engine only calculates shifts. Tests: `ADayOff_WithNoPunches_RaisesNoException_EvenLongAfterwards` (clock 30 days later, so the grace can't be the reason), `AnEmployeeWithoutASchedule_...`, and the contrast case `AScheduledDay_WithNoPunchesAtAll_...`. Punches on a day off are still unscheduled work: one `PendingReview` record (P7), and an exception only if their sequence is wrong.

## How reprocessing treats exceptions (invariants 2, 7)

- **Once.** An exception is matched on employee + date + shift + type + missing event type + related punch. A detected one with an *active* match (`Open` or `PendingCorrection`) is kept; otherwise it is opened. Reprocessing never duplicates.
- **Auto-resolve.** An `Open` exception that is no longer detected (the missing punch arrived late) is `Closed`, with `ResolutionMethod = AutoResolved` and `ResolvedAt`. Nothing is deleted.
- **`PendingCorrection` is left alone,** whatever is detected: its correction (P9) closes it.
- **A `Closed` exception is never reopened.** If the same problem comes back, a new exception is raised.

## The review gate (item 3, invariant 5, study §11.3)

A shift with an active exception (`Open` or `PendingCorrection`) is never `Approved`:

- a new record version is built `PendingReview` (`BuildRecord`, `hasActiveException`);
- an *unchanged* record that was `Approved` (or `Calculated`) is moved back to `PendingReview` in place. Status is the one field a reviewer changes in place (P7 §8), so this isn't a rewrite of the calculation. `Rejected` is kept.
- The day summary's status follows from its records, and its `ExceptionCount` counts the active exceptions.

Tests: `AnApprovalIsTakenBack_WhenAnOpenExceptionExists_...`, `APendingCorrectionException_IsLeftAlone_...`, and every detection test asserts `PendingReview`.

## Not done

- **Unlabeled punches** still left untyped after interpretation make the record incomplete, but raise no exception: they aren't "missing" and aren't in the prompt's list. A record can therefore still be `PendingReview` without an exception in that case. Needs a decision (a third exception type?).
- **Exceptions of a retired shift** (the shift is no longer scheduled that day) are auto-resolved if `Open`, as any exception that is no longer detected.
- **`Open` exceptions aren't raised for rows flagged `FlaggedUnrecognizedDevice`**: P7 leaves those punches out of the calculation until a person resolves them, so they can't be analyzed.
- **No endpoint or screen** lists exceptions; that is P9.
- **Not run against PostgreSQL.** I checked the migration SQL with `dotnet ef migrations script` (`ALTER TABLE ... ADD "MissingPunchGraceMinutes" integer NOT NULL DEFAULT 120;`, the table and its indexes) but did not start the Api, so the two tenant databases haven't been migrated.

## Changed beyond the prompt

- `ShiftAttendanceCalculator` now drops same-type repeats inside the duplicate window (above) and exposes the list it worked from (`ShiftCalculation.Punches`).
- The engine's constructor takes a `TimeProvider`.
- The test helpers of `AttendanceProcessingEngineTests` moved to `AttendanceEngineTestBase` (with `FixedTimeProvider`) so the new tests share them. The P7 tests are otherwise unchanged and still pass.

## Verification

- `dotnet build robeem.attendance.slnx`: 0 warnings, 0 errors.
- `dotnet test robeem.attendance.slnx`: 118 pass (the 100 from P7, plus 18 in `AttendanceExceptionDetectionTests`: Scenario 3; missing check-in; missing check-out before and after the grace, and once only; the policy's grace; missing break-in, and its wait; check-out before check-in; two check-ins inside and outside the window; break-in on a no-break shift; missing break punches; day off; no schedule; no-show; the approval taken back; auto-resolve; pending correction left alone).
- `dotnet format robeem.attendance.slnx --verify-no-changes`: passes.
