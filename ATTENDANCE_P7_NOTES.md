# Attendance P7 — processing engine: notes

## What Prompt 7 added

- **Entities:** `AttendanceRecord` and `AttendanceDaySummary`, with their enums `AttendanceRecordStatus` and `AttendancePunchSource`. EF configuration and migration `AddAttendanceRecords`.
- **Services** in `src/robeem.Application/Attendance`:
  - `IAttendanceProcessingEngine` / `AttendanceProcessingEngine`, which does the I/O, versioning and rollup;
  - `ShiftAttendanceCalculator`, the pure calculation of one shift.

This file records where the result differs from, or fills a gap in, the prompt's spec.

## The study's scenarios were reconstructed, not copied

**The study is not in the repository, so Appendix B could not be copied "verbatim".** The tests reproduce the **outcomes** the prompt quotes, from inputs I reconstructed:

| Scenario | Inputs used | Outcome asserted |
|---|---|---|
| 1, standard day | Shift 08:00–17:00 with a 12:00–13:00 break. Punches 08:00 in, 12:00 break-out, 13:00 break-in, 17:00 out. | 8:00 worked, 1:00 break, 0 late, 0 early, 0 overtime |
| 2, late with overtime | Same shift, `RequiresOvertimePreApproval`, grace 0. Punches 08:12 in, 12:00 break-out, 13:00 break-in, 18:35 out. | 12 min late, 9:23 worked, 1:23 overtime, PendingReview |
| 5, overnight | Shift 22:00–06:00, no break. Punches Monday 22:00 in, Tuesday 06:00 out. | AttendanceDate = Monday, 8:00 worked across midnight, Tuesday claims nothing |

The 08:00–17:00 / 12:00–13:00 shift is inferred from three things:

- Scenario 1's "8:00 worked / 1:00 break";
- Prompt 9's Scenario 3, where "BreakOut [is] corrected to 12:00";
- Scenario 2's numbers: 08:12 in and 18:35 out give exactly 12 late and 1:23 overtime with grace 0.

**Please compare the inputs with Appendix B.** If the study used a grace period or different punch times, only the test data needs to change.

## Choices to review

### 1. Which punches belong to which shift (§17.1)

Each shift claims the punches whose local time falls between:

- 4 hours before its start, and
- 8 hours after its end.

When a neighboring shift is closer, the midpoint between the two shifts is the boundary instead. The engine resolves the day before and the day after as well, so:

- an overnight shift claims the next morning's check-out;
- processing the next day doesn't claim it again.

The 4 h and 8 h windows are my assumption; the study's §17.1 parameters were not available.

### 2. Elapsed time versus schedule comparisons

- **Working and break time** are measured on the UTC instants. A shift across midnight, or across a daylight-saving change, needs no special arithmetic.
- **Lateness and early departure** compare the local wall-clock times with the scheduled start and end.
- **The scheduled working time** comes from `Set.TimeSpan(start, end, breakStart, breakEnd)`, which already handles overnight shifts. Nothing reimplements the rollover.

### 3. The formulas

| Measure | Rule |
|---|---|
| Late | `max(0, actual − scheduled − grace)`, as specified. The grace is subtracted; it is not a threshold that zeroes small lateness. |
| Early departure | The same, with the early-departure grace. |
| Overtime | `working − scheduled`. Zero below `OvertimeThresholdMinutes`, which the prompt didn't mention; I read the threshold as a minimum. Only computed for a **complete** shift: with a punch missing, the time worked isn't known. |
| Minute values | Rounded down to whole minutes. |
| Rounding | `RoundingRuleMinutes` rounds each punch to the nearest multiple, for the **calculation only**. The record keeps the real punch times. |

### 4. Pairing

Punches are paired in time order: check-in, then break-out/break-in pairs, then check-out.

- **Several pairs in one shift** (stepping out and back) add up.
- **An out-of-sequence punch** (a second check-in, a check-out during a break) is skipped, and the shift is marked incomplete.
- **`Actual*` fields** hold the first check-in, the last check-out, and the first break pair.
- **`Other` events** (for example door access) are ignored.

### 5. Missing punches

The prompt says to assume all punches are present. The engine still never invents one (invariant 5):

- A shift is **complete** only when it has a check-in and a check-out, plus a break-out and a break-in if the shift has a break.
- An incomplete shift gets a record with what is known, overtime 0, and status **PendingReview**.
- **A missing break is not auto-deducted:** "8:00 in, 17:00 out" on a break shift is 9:00 worked and PendingReview.
- **A scheduled shift with no punches at all** gets an empty PendingReview record, so the absence is visible.

P8 will attach the exceptions to these records.

### 6. Status

A record is **Approved** when it is scheduled, complete, and has no overtime needing pre-approval. Otherwise it is **PendingReview**. `Calculated` is used only for a day summary with no records. `Rejected` is never set by the engine.

Auto-approval follows the prompt's "PendingReview instead of auto-Approved". Payroll (P11) still requires the day to be exception-free.

### 7. Work outside any shift

Punches that no shift claims, for example on a day off, become a record with no shift (`TimeScheduleShiftId` null), no lateness or overtime, and status PendingReview. **Whether such work counts as overtime is a business rule not given in the prompt;** a reviewer decides for now.

### 8. Versioning: records are never rewritten (study §6.3)

- **Unchanged recalculation:** the current record is kept as it is, including a reviewer's status. Reprocessing is therefore safe, and doesn't reset approvals. The comparison covers every calculated field except status.
- **Changed recalculation:** a new record is written with the next `Version`. The previous one gets `IsCurrent = false` and `SupersededByRecordId` pointing at the new one, and keeps its values.
- **A shift no longer scheduled that day:** its current record is retired (`IsCurrent = false`) without a successor.

Schema support:

- `IsCurrent` and `SupersededByRecordId` were added; the spec had only `Version`.
- A unique index on `(TenantId, EmployeeId, AttendanceDate, TimeScheduleShiftId, Version)`: if two runs process the same day concurrently, one of them fails instead of both versions becoming current.
- All of a day's changes are saved together, in one transaction.

### 9. Field types

| Field | Type | Why |
|---|---|---|
| `WorkingHours`, `BreakDuration`, `Total*Hours` | `TimeSpan`: `interval` in PostgreSQL, text in SQLite | Exact. 1:23 is 1:23, not 1.3833 hours. |
| `Late`, `EarlyDeparture`, `OvertimeMinutes` | `int` minutes | As specified. |
| Scheduled and actual times | Local `DateTime` (`timestamp without time zone`) | Like the schedule. |
| `TimeScheduleShiftId` | Nullable | For work outside any shift. |
| Punch sources | Adds `Inferred` to the spec's Normalized/Corrected | Invariant 6 requires inferred types to be distinguishable. |

### 10. Unlabeled punches (Sequential mode)

P5 keeps unlabeled punches when the policy's interpretation mode is `Sequential`. The engine types them only when **all** of a shift's punches are unlabeled and their count fits:

- 2 punches become check-in and check-out;
- 4 punches, on a shift with a break, become check-in, break-out, break-in and check-out.

Their source is `Inferred`. The normalized rows are not changed. Anything else stays unlabeled, which leaves the shift incomplete and PendingReview. `RulesEngine` mode is not implemented and behaves the same way.

### 11. Punches the engine leaves out

These punches are not used:

- duplicates (anything other than `Unique`);
- rows with `ProcessingStatus = Excluded`;
- rows flagged `FlaggedUnrecognizedDevice`. Their local time is UTC (P5), so comparing them with the schedule would be wrong. A person resolves them first, and the shift shows as incomplete meanwhile.

### 12. Day summary

The summary is a derived rollup, updated in place each time the day is processed. Its status is the rollup of the day's current records:

| Records | Summary status |
|---|---|
| None | Calculated |
| Any rejected | Rejected |
| All approved | Approved |
| Otherwise | PendingReview |

`ShiftCount` counts all current records, unscheduled work included. `ExceptionCount` is left for P8.

## Not done

- **Normalized rows are not marked `Processed`.** The prompt didn't ask for it, and nothing triggers processing yet. A trigger is P10's job.
- **The shift's `Overtime` flag (`TimeShiftEntity.Overtime`) is not used.** Its meaning isn't documented: "overtime allowed" or "this is an overtime shift"?
- **Corrections (P9) aren't folded in yet.** `AttendancePunchSource.Corrected` is ready for them.

## Verification

- **Build:** `dotnet build` finishes with 0 warnings and 0 errors.
- **Format:** `dotnet format --verify-no-changes` passes.
- **Tests:** all 100 pass. The new file is `tests/robeem.Tests/Attendance/AttendanceProcessingEngineTests.cs`, with 12 tests:
  - Scenarios 1, 2 and 5;
  - Scenario 2 without pre-approval, which is Approved;
  - an unchanged reprocess keeps the record;
  - a changed reprocess writes version 2 and retires version 1;
  - a multi-shift day gives two records and one summary of both;
  - lateness grace;
  - rounding;
  - missing break punches are neither invented nor deducted;
  - work on a day off;
  - Sequential inference.
- **Migration on PostgreSQL:** running the Api applied `AddAttendanceRecords` to both tenant databases.
- **The engine on PostgreSQL:** a temporary test, since deleted, ran it against `robeem_demo` inside a transaction that was rolled back. Nothing was left behind; this was checked. It used an overnight shift with Beirut-time punches:
  - run 1, with only the check-in: version 1, PendingReview;
  - run 2, after a 06:20 check-out: version 2, 8:20 worked, 20 minutes overtime, Approved. Version 1 was retired and linked to version 2;
  - run 3: unchanged.

  This confirms the `interval` columns, the versioning save order and the summary on Npgsql.
