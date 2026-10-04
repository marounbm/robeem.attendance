# Attendance P6 — schedule resolution: notes

Prompt 6 added the following, all in `src/robeem.Application/Attendance`, with the service registered as scoped in `AddRobeemApplication`:

- `IScheduleResolutionService` / `ScheduleResolutionService`;
- the result types `ShiftResolution`, `ShiftResolutionOutcome` and `ResolvedShift`;
- the `IsOvernight(this TimeShiftEntity)` extension.

No schema change, no migration. This file records where the result differs from, or fills a gap in, the prompt's spec.

## Choices to review

### 1. A scheduled day returns a list of shifts

The spec's result held one shift. But `TimeScheduleLine`'s unique key is `(TimeScheduleId, TeamId, CycleDay, ShiftId)`, so one team can have several shifts on one cycle day: a split shift such as 07:00-11:00 plus 17:00-21:00. Returning one shift would silently drop the others.

So the result is a `ShiftResolution` per date:

| Field | Meaning |
|---|---|
| `Outcome` | `Scheduled`, `DayOff` or `NoScheduleConfigured` |
| `Date` | The date resolved. |
| `CycleDay` | Null when the employee has no schedule. |
| `Shifts` | The shifts, ordered by start time. Empty unless `Scheduled`. |

Each `ResolvedShift` carries:

- the fields from the spec: `TimeScheduleShiftId`, `StartTime`, `EndTime`, `BreakTime`, `BreakStartTime`, `BreakEndTime`, `Overtime`, `IsOvernight`;
- `StartsAt` and `EndsAt` as local wall-clock times. `EndsAt` is computed with `Set.TimeSpan`, which already moves an overnight end to the next day. P7 can compare punches with these directly.

### 2. The all-inclusive team's lines are included (CLAUDE.md §3.2)

The prompt says there is no all-inclusive fallback branch to write, and there isn't one. But CLAUDE.md §3.2 states a resolution rule, not a fallback: "lines of the schedule's all-inclusive team apply to [an assigned employee], because that team contains everyone on the schedule. So do lines of the team they are explicitly in."

The line query is therefore `TeamId == employee's team OR team is all-inclusive`.

- **Today:** assignment always uses the all-inclusive team, so this gives exactly the same result as matching the employee's team alone.
- **If an employee is ever placed in a specific team:** they still get the schedule-wide lines.
- **Test:** `EmployeeInASpecificTeam_GetsTheirTeamsLines_AndTheAllInclusiveTeamsLines` covers this.

If you'd rather resolve strictly by the employee's own team, it is a one-line change. CLAUDE.md §3.2 would then need updating to match.

### 3. Method names end in `Async`

They are `ResolveShiftAsync` and `ResolveShiftsInRangeAsync`, not the spec's `ResolveShift` / `ResolveShiftsInRange`. Every async method in the solution ends in `Async`.

### 4. The range method loads the roster once

The prompt suggested calling `ResolveShift` once per day, which would mean four queries per day. A pay period across many employees would multiply that.

`ResolveShiftsInRangeAsync` instead:

1. loads the assignment, the schedule, the applicable lines and their shifts once;
2. resolves each date in memory.

`ResolveShiftAsync` is the same code run on a one-day range, so the two always agree; a test checks this. The range includes both ends, and a range that ends before it starts throws `ArgumentException`.

### 5. Dates

The time of day of the input is ignored; only the date part is used. Results carry an unspecified-kind date (CLAUDE.md §2.8).

## Not covered

- **A shift whose start equals its end** (for example `00:00-00:00`) is treated as zero hours, not 24, because `Set.TimeSpan` returns 0, and it is not overnight. Nothing seeds such a shift. If 24-hour shifts are needed, `Set` and `IsOvernight` must agree on a convention first.
- **`TimeSchedule.LastScheduleDate` / `LastScheduleDay`** are used as the supplied `GetCycleDay` uses them. The tests use the seeded default (`DateTime.MinValue`, cycle anchored on `FirstCycleDay`).

## Verification

- **Build:** 0 warnings and 0 errors.
- **Format:** `dotnet format --verify-no-changes` passes.
- **Tests:** all 88 pass. The new file is `tests/robeem.Tests/Attendance/ScheduleResolutionServiceTests.cs`, with 11 tests:
  - The four scenarios the prompt asked for, run against the real seeded "Standard Week" via `HrSeedContributor`:
    - a full Monday-to-Sunday cycle: Mon–Fri scheduled 09:00–17:00 with the 13:00–14:00 break, cycle days 1–7, weekend off;
    - an employee without an assignment gets `NoScheduleConfigured`;
    - a cycle day with no line is `DayOff`;
    - a 22:00–06:00 shift is overnight, ends at 06:00 the next day, and lasts 8 hours by `Set.TimeSpan`.
  - Three more:
    - a single-day call matches the range call;
    - an employee in a specific team also gets the all-inclusive team's lines, but not other teams' lines;
    - an inverted range throws.
  - Four `IsOvernight` cases.
- **How it was built:** a `robeem.Web` process (PID 15000) was running and locked `src/robeem.Web/bin`, which the test project references. Build and tests were therefore run with `--artifacts-path` into a scratch folder instead of the normal `bin` folders. The running process was left alone.
