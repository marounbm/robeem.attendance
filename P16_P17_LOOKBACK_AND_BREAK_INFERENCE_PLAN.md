# Combined plan: restore break order (P15a), holidays and leaves (P15b), break inference (P16), look-back job + no-show + hourly staff (P17), visible and editable rules (P18)

**Status: approved 2026-11-07; implementation in progress. This file is the single source for these prompts; `PROMPT_PLAN.md` has an addendum with the checklist that points here.** After approval, P15a, P15b, P16 and P17 are appended to `PROMPT_PLAN.md` (plus a status line), and `CLAUDE.md` §2.9/§4/§6, `TEST_COVERAGE.md` are updated as part of implementation. **Order of work: P15a first** (small, independent fix), then **P15b** (holiday and leave tables, screens and the calendar that reads them), then **P16** (break inference, which uses the calendar), then **P17** (look-back job, which uses it too), then **P18** (a rule table and screen through which HR sees and edits how the system infers breaks and decides what becomes an exception; it takes over the decisions P16 and P17 hard-code).

> **Source caveat.** I could not find the document "Attendance — Inferring Breaks on Devices Without a Break Type, Adjusted for Approved Leave and Overnight Shifts" in the repository (no match in any `.md`; Google Drive is not authorized in this session). **P16 below is drafted from its title and from the existing code** (`UnlabeledPunchInterpretationMode.Sequential`, `ShiftAttendanceCalculator`, `ResolvedShift.BreakStartsAt`, the break rules). Please check it against the document; every rule I had to choose is marked *(assumed)*.

## Context

- `PROMPT_PLAN.md` holds P0–P15, all done. P15a, P16 and P17 follow in the same prompt style.
- **Break gap.** A device that sends only in/out for a shift with a scheduled break gives `CheckIn, CheckOut, CheckIn, CheckOut`. The calculator reads that as two work segments with no break, so `NoBreak` / missing break-out and break-in exceptions are raised although the break was taken. `Sequential` interpretation only covers punches that are *all unlabeled* (2, or 4 on a shift with a break).
- **No-show trigger gap.** `AttendanceExceptionDetector.Detect` already treats "a scheduled shift without a single punch" as a no-show, but `PendingDayProcessor` only runs the engine for days with *pending normalized punches*. A day nobody punched is never evaluated. The same gap leaves a check-in with no check-out unevaluated once the grace passes, unless another punch arrives.
- **Nothing exists** for leave, holiday or pay basis (grep-verified); `Profile` has no pay basis. P15b adds the holiday and leave tables next to `Profile` (schema `HR`); P17 adds the pay basis.
- Jobs: three `IScheduledJob`s in `src/robeem.Api/Attendance/Jobs`, registered in `Program.cs`; options in `AttendanceSyncOptions`.

## Decisions (from your answers)

- A leave or holiday may be entered *after* the exception was raised; once it is in the system, the open exception closes automatically (P17).
- Hourly (wage) staff are marked as such, are **not compared to a schedule**, never get a missing-day exception, and raise an exception only when worked hours cannot be calculated (P17).
- Holidays and leaves are **read from two new tables next to `Employees` (`Profile`)**, in the same schema (`HR`), with simple screens to maintain them (P15b). Holidays start filled with the Lebanese 2026 calendar. Leaves are entered by HR and count as approved the moment they are saved (there is no approval step).

## Shared piece: the leave and holiday calendar (built in P15b, used by P16 and P17)

`IEmployeeAbsenceCalendar` lives in **`src/robeem.Application/HR/Absence`**, because HR owns the data and **HR must not depend on Attendance** (CLAUDE.md §4). Attendance reads it, which is the allowed direction.

`GetAsync(employeeIds, from, to)` returns an `AbsenceCalendar`; `calendar.For(employeeId)` returns that employee's `ExcusedPeriod(Kind, Reference, Start, End, IsWholeDay)` list, the tenant's holidays and the employee's leaves together. (Holidays apply to everyone, so they are held once and merged in by `For`, not copied per employee.)

- `Kind` is `Leave` or `Holiday`. A whole-day entry has `IsWholeDay = true` and one entry per date; part-day leave has local wall-clock `Start`/`End` (may cross midnight). `Reference` is the holiday's name or the leave's type, for the audit note.
- The one implementation, `HrAbsenceCalendar`, reads the P15b tables through `IRobeemDbContext`, so it is tenant-filtered like everything else. There is no no-op default; tests use the real tables or a fake.
- **Matching rules** (decided once, used by P16 and P17): a *whole-day* entry excuses a shift by the shift's **start date** (the date its record carries); a *part-day* entry is matched by **instants**, intersected with the shift's `StartsAt..EndsAt`. The engine asks for `[day-1, day+1]`, the same range it uses to resolve shifts, so a period after midnight on an overnight shift is seen.

---

## Prompt 15a — Restore the punch order: check-in, break-out, break-in, check-out

**Problem.** The *Correct a day* feature (commit `c6b85ee`, "completeness and order checks") enforces the order **check-in, break-in, break-out, check-out**. That is backwards: a person goes *out* on a break and comes back *in*. Everything else in the solution uses break-out first (`NormalizedEventType`, `ShiftAttendanceCalculator`, `AttendanceExceptionDetector`, `AttendanceProcessingEngine.AttendanceTypes`, the Normalized transactions filter). So a day correction that passes the check (break-in 12:00, break-out 13:00) is read by the engine as a break-in with no break-out, and the exception comes back after the correction is applied.

**Fix: make the correction rules follow the real order. Display and validation only; no schema, no migration, no change to the engine or calculator.**

1. `AttendanceDayCorrectionService` (`src/robeem.Application/Attendance`):
   - `AttendanceTypes` (line 17) becomes `CheckIn, BreakOut, BreakIn, CheckOut`. It orders `RequiredPunches` and breaks ties between punches at the same minute.
   - `OutOfOrder` transitions: after a check-in or a break-in → break-out or check-out; after a break-out → break-in only (the detector raises a missing break-in for a check-out during a break); after a check-out → check-in. Several breaks in a day stay allowed. Update the message to "The order is check-in, break-out, break-in, check-out." and the doc comment above it.
2. Web: `Views/Exceptions/Correct.cshtml` (the `order` array, the `next` map, the explanatory text at line 205 and the two messages in the script) and `Models/DayCorrectionViewModel.EventTypes` (the dropdown order). The browser check mirrors the Api's; the Api stays the authority.
3. Contracts: the `RequiredPunches` doc comment in `DayCorrectionView.cs` (line 19).
4. Docs: `CLAUDE.md` roadmap bullet "Correct a whole day" (the sentence "the order check-in, break-in, break-out, check-out"). Nothing else in the repository states the old order.
5. Tests, `tests/robeem.Tests/Attendance/AttendanceDayCorrectionTests.cs`:
   - `ThePunchesMustFollowTheOrder_…` is renamed `…CheckIn_BreakOut_BreakIn_CheckOut` and its data swapped: break-out at 12:00 and break-in at 13:00 are in order; the reverse is refused with the new message.
   - `AShiftWithABreak_NeedsTheBreakPunchesToo_…`: `RequiredPunches` is `["CheckIn","BreakOut","BreakIn","CheckOut"]`, the refusal reads "a break-out, a break-in", and the settling edits add the break-out at 12:00 and turn the 13:00 tap into the break-in.
   - New: the day as corrected is also handed to the engine and no break exception is raised. This closes the gap that let the reversed order through, because the UI checks and the engine disagreed.
   - Kept as they are: single-partner messages ("a break-out" / "a break-in"), multiple breaks in one day.
6. Risk to state at the end: a day correction already **approved and applied** under the reversed order (break-in before break-out) is stored as such and re-raises an exception on its next recalculation. No data is rewritten (corrections are additive, invariant 7). Check the Development databases for applied corrections whose break-in precedes the break-out; fix them with a new correction.

Definition of done: standard DoD, plus the Web test of the Correct page and the Api test of the day-correction endpoint still pass with the new order.

---

## Prompt 15b — Holiday and leave tables, simple screens, and the calendar that reads them

Depends on nothing in Attendance. It follows the HR Employees pattern end to end (`Profile` → `EmployeeService` → `EmployeesController` → Web `EmployeesController`), so a reviewer sees the same shapes.

### 1. Tables (schema `HR`, next to `Profile`; namespace `robeem.DataModel`; both `ITenantScoped`)

`PublicHoliday : Entity, ITenantScoped, ISeedData` (`src/robeem.Domain/HR`)
- `Date` (date only) and `IsProvisional` (bool: a date that follows the moon and may move; shown as "to be confirmed"). The name is the inherited `Title` (required, max 255). One row per day: a three-day Eid is three rows.
- Unique index `(TenantId, Date)`: one holiday per date. `ISeedData` is needed so the seeder may add them.

`EmployeeLeave : Entity, ITenantScoped` (`src/robeem.Domain/HR`)
- `EmployeeId` / `Employee` (`Profile`), the standard pattern (CLAUDE.md §2.4).
- `LeaveType` enum stored as its name: `Annual`, `Sick`, `Unpaid`, `Other` (start small; adding a member is cheap).
- `StartDate`, `EndDate` (inclusive, `EndDate >= StartDate`).
- Part-day leave: `IsPartDay` (bool) with `StartTime` / `EndTime` as `"HH:mm"` strings (MaxLength 5, the `TimeShiftEntity` convention, parsed with `Set.ParseTime`). Only on a single date (`StartDate == EndDate`); an end earlier than the start runs into the next day, as `Set.TimeSpan` counts it.
- `Notes` (optional, max 500). `Title` is a `[NotMapped]` computed label (`"Annual — 3 Aug – 7 Aug"`), §2.5.
- Indexes `(TenantId, EmployeeId, StartDate)` and `(TenantId, StartDate, EndDate)`. Foreign key `Restrict`, as everywhere.
- No status column: a leave HR saves **is** approved. Approval, balances and accruals are the (future) Leave module's.

EF configurations in `src/robeem.Infrastructure/Persistence/Configurations/HR` (next to `ProfileConfiguration`), `DbSet`s `PublicHolidays` and `EmployeeLeaves` on `IRobeemDbContext` / `RobeemDbContext`, one migration `AddHolidaysAndLeaves`. Both tables are new, so there is no data step.

### 2. Default data: the Lebanese 2026 calendar

`HolidaySeedContributor` (`src/robeem.Application/HR/Seeding`, `Order` 110, next to `HrSeedContributor`). Like it, it runs **only while the tenant has no holiday at all**, so a holiday an administrator removes never comes back. Only the 2026 list is seeded; later years are added on the screen.

> **Not verified.** The Banque du Liban page (the authoritative banking-holiday list) answered 403 to my fetch, and the public calendars I found disagree in places. The list below is my best expected list. **Check every row against the official decree or the BdL circular before the seed ships**; the seed is editable data, so a wrong date is a screen edit, not a release.

| Date (2026) | Holiday | Basis |
|---|---|---|
| Thu 1 Jan | New Year's Day | fixed |
| Tue 6 Jan | Armenian Christmas | fixed |
| Mon 9 Feb | Saint Maroun's Day | fixed |
| Fri 20 – Sun 22 Mar | Eid al-Fitr (3 days) | lunar, provisional |
| Wed 25 Mar | Annunciation | fixed |
| Fri 3 Apr | Good Friday (Western) | Easter-based, computed |
| Mon 6 Apr | Easter Monday | Easter-based; confirm it is official, drop it if not |
| Fri 10 Apr | Good Friday (Orthodox) | Easter-based, computed |
| Fri 1 May | Labour Day | fixed |
| Wed 6 May | Martyrs' Day | fixed |
| Mon 25 May | Resistance and Liberation Day | fixed |
| Wed 27 – Thu 28 May | Eid al-Adha | lunar, provisional (a third day, 29 May, may be decreed) |
| Tue 16 Jun | Islamic New Year | lunar, provisional (16 or 17 Jun) |
| Thu 25 Jun | Ashura | lunar, provisional (25 or 26 Jun) |
| Sat 15 Aug | Assumption | fixed |
| Tue 25 Aug | Prophet's Birthday (Mawlid) | lunar, provisional (25 or 26 Aug) |
| Sun 22 Nov | Independence Day | fixed (one source also lists Mon 23 Nov as the government's observed day; confirm) |
| Fri 25 Dec | Christmas Day | fixed |

Easter Sunday (5 and 12 Apr) is a Sunday and needs no row. A holiday on a day off excuses nothing, so weekend rows are harmless. Every lunar row is `IsProvisional = true`, which the screen shows as "to be confirmed" until HR clears the flag.

### 3. Application (`src/robeem.Application/HR`)

- `IHolidayService` / `HolidayService` (`Holidays/`): `ListAsync(year)`, `GetAsync(id)`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`. Create takes a date and an optional **last day**, and adds a row per day (a multi-day Eid in one go). Refuses a date that already has a holiday. Result types follow `EmployeeSaveResult` (field-keyed errors, not found).
- `ILeaveService` / `LeaveService` (`Leaves/`): `ListAsync(filter)` → `PagedResult<LeaveListItem>`, `GetAsync(id)`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`. Validation, all field-keyed:
  - the employee exists (tenant-filtered) and the dates are valid, with a sanity limit of 366 days per leave;
  - part-day: one date, valid `HH:mm`, start different from end; whole-day leave clears the times;
  - **no overlap** with another leave of the same employee: whole-day ranges may not intersect, and part-day times on one date may not intersect each other or a whole-day leave on that date. (It is a service rule, not a database constraint.)
  - Editing re-validates with the leave itself left out of the overlap test.
- `IEmployeeAbsenceCalendar` / `HrAbsenceCalendar` (`Absence/`), as described under *Shared piece*: whole-day leave → one entry per date inside `[from, to]`; part-day → one entry with its `Start`/`End`; every holiday in range → a whole-day `Holiday` entry for everyone.
- Registered in `AddRobeemApplication`; the seeder in the seed contributors list.

### 4. Api (`src/robeem.Api/Controllers`; DTOs in `src/robeem.Contracts/HR/Holidays` and `…/Leaves`, never entities)

Both controllers are `[Authorize(Roles = AttendanceRoles.HRAdministrator)]`, like `EmployeesController`; every action names its roles (a test enforces it).

- `api/holidays`: `GET ?year=` (the year defaults to the current one), `GET {id}`, `POST`, `PUT {id}`, `DELETE {id}`.
- `api/leaves`: `GET ?employeeId&search&from&to&type&page&pageSize` → `PagedResult<LeaveListItem>`, `GET {id}`, `POST`, `PUT {id}`, `DELETE {id}`. `api/employees/options?search=` (new, at most 20 matches by number or name, ignoring case) feeds the employee picker.

### 5. Screens (`src/robeem.Web`: controllers, views, models, typed `ApiClients`, all HTTP to the Api)

- **Holidays** (`HolidaysController`): a year selector (default the current year, a link to each year that has rows, plus "this year / next year"), a table of date, weekday, name and a "to be confirmed" badge, a **Remove** button per row (POST with confirmation), an **Edit** link, and an **Add holiday** form (date, optional last day, name, "to be confirmed"). It stays unpaged: a holiday list is a small catalog bounded by its year (CLAUDE.md §2.9).
- **Leaves** (`LeavesController`): a **paged** list (§2.9: default 20, sizes 10/20/50/100/200, the shared `_Pager` keeping every filter) filtered by employee (a search box, not a dropdown), date range and type, newest first with a unique tie-break on the id; **Add**, **Edit** and **Remove** (POST with confirmation). The form has the employee (search then pick; reached with the employee already chosen from the Employees grid or edit page, which get a new **Leave** link), type, first and last day, a "part of a day" checkbox that shows the two time fields (hidden for a whole day), and notes. A leave in the past can be edited or removed; the page says that changing leave after days were calculated is picked up by the nightly look-back (P17) or the next recalculation.
- Navigation (`NavigationMenu.cs`), in the HR section after *Time schedules*: **Holidays** and **Leaves**, both `Hr`.

### 6. Effect on attendance, and what is deliberately not done

- Nothing in Attendance is called when a holiday or leave is saved: HR cannot depend on Attendance. P16's engine reads the calendar when it calculates a day, and P17's nightly look-back finds days whose excused amount changed (including a leave that was **removed or shortened**, which must bring a no-show back; see P17 step 3).
- No Attendance audit entry (the audit log is Attendance's, and employee edits are not audited either). Holidays and leaves carry the base `CreatedBy/ModifiedBy`. *(open point: ask whether HR changes should be audited)*
- Managers and employees do not see these screens (HR only). *(open point: an employee viewing their own leave)*
- Copying one year's fixed-date holidays to the next year is not included; it is a good follow-up.

### 7. Tests

`tests/robeem.Tests/Attendance` is for attendance; these go beside the HR tests (`HR/`):
- **Holiday service:** create, multi-day create, duplicate date refused, edit, delete; list by year.
- **Leave service:** create/edit/delete; overlap refused (whole-day vs whole-day, part-day vs part-day, part-day vs whole-day, and an edit that does not collide with itself); part-day on a range refused; bad times refused; unknown employee refused. **Paging per §2.9:** the pages together are the whole list once and in order; the total follows the filters; a page past the end answers the last page; the size is clamped.
- **Calendar:** a holiday excuses every employee, a leave only its own; whole-day range clipped to `[from, to]`; part-day carries its times; a part-day leave across midnight; another tenant's rows never appear (tenant isolation).
- **Seeding:** the 2026 list is present, lunar rows are provisional, a second run adds nothing, a tenant that already has a holiday is left alone, a removed holiday is not re-added.
- **Api:** each endpoint's roles (the existing every-action-names-its-roles test must pass), a 400 carries field-keyed errors, 404s.
- **Web:** the Leaves pager keeps every filter and the page size; the part-day fields show only when ticked; the employee picker posts the chosen id.
- **Architecture:** the existing tests keep HR free of Attendance types; migration applies (opt-in PostgreSQL test).

Definition of done: standard DoD; the two screens work in Development on the demo tenant (the 2026 holidays are there after startup).

---

## Prompt 16 — Infer breaks on devices that send no break type, adjusted for approved leave and overnight shifts

Depends on P15a (the break order) and P15b (the holiday and leave tables and the calendar). Adds the `ExcusedMinutes` on the record that P17 relies on.

**Rule of the feature (CLAUDE.md §1.5–1.7):** a break is *interpreted* from punches that exist, never invented; the result is always marked `Inferred`; the normalized rows are never edited. The interpretation lives in the calculation, like `Sequential`.

1. **Use the calendar.** `IEmployeeAbsenceCalendar` and its HR-table implementation come from P15b. The engine injects it, loads the day's excused periods for `[day-1, day+1]` once per `ProcessEmployeeDayAsync`, and passes the ones that intersect each shift to `ShiftAttendanceCalculator`.
2. **Policy.** `AttendancePolicy.BreakInferenceMode` (`Off` default, `FromGap`), `InferredBreakWindowMinutes` (default 90: how far from the scheduled break a gap may start or end), `InferredBreakMaxMinutes` (default 120). The minimum length is the existing `IgnoreBreaksShorterThanMinutes`. Policy form (`_PolicyForm.cshtml`), `PolicyInput`, `PolicyListItem`, `AttendancePolicyService` validation, EF configuration, migration; existing policies get `Off`.
3. **Inference step** in `ShiftAttendanceCalculator`, after de-duplication and before pairing (next to `InterpretUnlabeled`). *(assumed rules)*
   - **Build for P18:** write it as its own pure class (`BreakInferrer`: punches, shift, excused periods and a `BreakInferenceParameters` record in; the retyped punches and the reason out), with every number it uses in that one record and no hidden constants. `ShiftAttendanceCalculator` only calls it. P18 then moves the call behind a rule without rewriting the logic.
   - Only for a shift with `BreakTime = true`. Unscheduled work has no scheduled break to anchor to, so nothing is inferred; the same holds for hourly staff once P17 adds the pay basis (they are never matched to a shift), and their out-to-in gaps are simply time not worked.
   - Candidates are the internal `CheckOut → CheckIn` gaps (device-labeled in/out, alternating). A gap qualifies when its length is between the minimum and `InferredBreakMaxMinutes` and it lies within `InferredBreakWindowMinutes` of the scheduled break. The scheduled break is `ResolvedShift.BreakStartsAt` / `BreakMinutes`, **instants, not dates**.
   - Exactly one qualifying gap: retype its two punches `BreakOut` / `BreakIn` with `Source = Inferred`. Zero or several qualifying gaps (ambiguous): infer nothing; the existing rules apply and the day is left for review.
   - Odd punch counts (for example in, out, in) are never completed: the existing missing-check-out exception stands. A gap shorter than the minimum is treated as a short break (time stays work), the same as for labeled breaks.
   - Existing break rules then apply to the inferred break (long, late, early per the policy flags); `NoBreak` is naturally not raised, because a break was found.
   - The record stores `BreakInferred` (bool) so grids can show it. It is part of the version comparison. The Correct-a-day view labels the two punches "treated as a break (inferred)".
4. **Approved leave** (from the port) *(assumed)*:
   - A gap covered by part-day leave (any overlap with an excused period, or more than half the gap; pick one rule at implementation and test it) is **not a break**: it is excused time. Not inferred, not counted as break, no `LongBreak` / `EarlyBreak`. Worked hours are the segments, as now.
   - Leave at the start of the shift reduces `LateMinutes` by the overlap of `shift start..check-in`. Leave at the end reduces `EarlyDepartureMinutes` by the overlap of `check-out..shift end`.
   - Leave over the scheduled break window means `NoBreak` is not raised.
   - Overtime is measured against scheduled minutes **minus** excused minutes. The record stores `ExcusedMinutes` (part of the version comparison); P17's look-back compares it with the calendar to find days that need recalculating.
   - Until P17 ships, leave submitted after a day was calculated is picked up only when that day is recalculated for another reason (a new punch, a correction, a manual reprocess).
   - Whole-day leave or holiday **with punches** (worked on a day off): processed normally; whether it counts as overtime is left to the pre-approval policy. *(open point)*
5. **Overnight shifts.**
   - The break is anchored by `BreakStartsAt`, which already moves a break earlier than the shift start to the next day (22:00–06:00 with a 02:00 break). The gap test uses instants, so a gap at 01:30 after midnight is found.
   - Punches are claimed by the engine's shift window; the record is dated on the shift's start date.
   - The calendar is asked for `[day-1, day+1]`, so part-day leave after midnight intersects the shift. Whole-day leave matches the start date. Late/early comparisons use local wall-clock like the existing code, so DST fall-back is not specially handled.

**Tests (P16):** 4-punch in/out day with a gap near the scheduled break gets `BreakOut`/`BreakIn` marked `Inferred` and no missing-break exceptions; gap outside the window, too long, too short, two qualifying gaps, and 3 punches infer nothing; `Off` keeps today's behaviour exactly; unscheduled days infer nothing; part-day leave over the gap means no break and no long-break; leave covering late arrival / early departure lowers those minutes; leave over the break window means no `NoBreak`; overtime uses scheduled minus excused; recalculating after the calendar changed writes a new record version; overnight 22:00–06:00 with a 02:00 break (gap after midnight); overnight shift with part-day leave after midnight; version comparison treats `BreakInferred` / `ExcusedMinutes` changes as a new version; migration applies (opt-in PostgreSQL test).

---

## Prompt 17 — Look-back job: no-shows, device liveness, leave re-read, hourly staff

Depends on P15a, P15b (the holiday and leave tables and the calendar) and P16 (`ExcusedMinutes`).

1. **Hourly staff.** Add `PayBasis` (`Scheduled` default, `Hourly`) to `Profile` (enum in `src/robeem.Domain/HR`; HR must not use Attendance types), the employee form, DTOs and `EmployeeService`. Migration defaults existing rows to `Scheduled`.
   - `ScheduleResolutionService` returns `NoScheduleConfigured` for an `Hourly` employee even if a `TimeScheduleEmployee` row exists. Hourly pay is never compared with a schedule. (This also keeps P16's break inference away from hourly staff: no shift, no scheduled break.)
   - `AttendanceProcessingEngine.BuildRecord`: `shift is null` stops forcing `PendingReview` for hourly staff. A complete, exception-free day is `Approved` on worked hours. No late, early-leave, overtime-threshold or break findings (they need a shift).
   - Exceptions for hourly staff only when hours can't be calculated: check-in with no check-out after the grace, check-out with no check-in, invalid sequence. Never a no-show.
   - Edge: an hourly shift can cross midnight. Pair a check-in with the first check-out within `AttendancePolicy.HourlyMaxSpanHours` (new column, default 16), dated on the check-in day, instead of two exceptions.
2. **No-show exception.** New `AttendanceExceptionType.NoShow`: a scheduled shift (not a day off, not hourly) with zero punches once `shift end + MissingPunchGraceMinutes` has passed. It replaces the two `MissingPunch` exceptions the detector raises today for that case (old open ones are auto-resolved the next time their day is processed). One per employee, date and shift.
   - Ways out: punches arrive or are corrected (existing auto-resolve and day-correction paths; a day correction already works on any day with an open exception); **accepted with a reason** by the employee's manager or HR (extend `IAttendanceExceptionService.AcceptAsync`, as for break exceptions; an accepted no-show is not raised again for the same shift); or excused by leave/holiday (3).
3. **Calendar and no-shows.** The engine already loads the day's excused periods (P16). Before raising a no-show it checks them, and it **closes an open no-show** on an excused day with new `ResolutionMethod.ExcusedAbsence` (note = kind + reference, audited like any exception status change). One code path: the engine. An excused day with no punches writes no worked-hours record; payroll is unaffected (pay for leave is the leave module's).
   - **Removing or shortening a leave or holiday (P15b screens) must bring the no-show back.** A no-show closed as `ExcusedAbsence` does not block a new one (unlike an `Accepted` one, which does): when the day is recalculated and the excuse is gone, the engine raises a fresh `NoShow`. The look-back re-read (step 4d) finds such days because the record's `ExcusedMinutes` no longer matches the calendar, or because a closed `ExcusedAbsence` exception sits on a day the calendar no longer excuses.
4. **Look-back job** `AttendanceLookbackJob` (`attendance-lookback`) in `src/robeem.Api/Attendance/Jobs`, registered in `Program.cs`. Options `AttendanceLookbackOptions` (section `Attendance:Lookback`): `Enabled`, `Cron` (default `0 3 * * *`, after the 02:00 reconciliation so late punches are in), `WindowDays` (default 30, clamped 1..365), `MaxDaysPerRun`. Window = `[today - WindowDays, yesterday]`. Per tenant inside `ITenantScopeFactory.CreateScope`; logic in an Application service `IAttendanceLookbackService`, the job only decides when. Steps:
   a. **Device liveness.** For each active device of an active integration whose connection is not FileUpload, count and take the latest `ReceivedAt` of its `RawAttendanceTransaction` rows in the window. Zero = silent. Reported in the run summary and log; the run is marked partial-failed when any device is silent. It does **not** write `HealthStatus` (the sync job alone does, P13). The aggregate read of raw rows is audited once per run (`IAttendanceAuditTrail`). Optional second slice: `GET api/attendance/devices/activity` and a "Last punch" column on Devices.
   b. **Missing days.** Scheduled, active, non-hourly employees employed on the date (HireDate / TerminationDate) who have a shift and no `AttendanceDaySummary` yet. Skip any date on which the tenant has no punches at all (integration down, not an absence; reported as skipped). Hand each to `IAttendanceProcessingEngine.ProcessEmployeeDayAsync`, which decides (grace, calendar, accepted).
   c. **Incomplete days.** Days in the window whose current record is incomplete with no active exception: process again, so a missing check-out is raised after the grace (applies to hourly staff too).
   d. **Leave re-read.** Ask the calendar once for the window's excused periods. Re-process every employee-day that has an open `NoShow`, or whose current record's `ExcusedMinutes` (P16) differs from what the calendar now gives. Late-submitted leave or holidays then close the no-show and fix late/early/break findings. Also exposed as `IAttendanceLookbackService.ReexamineAbsencesAsync(employeeId?, from, to)` for the leave module to call on submission.
   - **Range edges.** The engine already resolves day-1..day+1 shifts and claims punches per shift window; do not reimplement the windows. A shift starting on the last day and ending after the range or after now is not yet due: nothing is raised and the next run picks it up. A shift starting the day before the window and ending inside it belongs to its start date (outside this window, already evaluated by earlier runs); its punches on the first day are claimed by it, not seen as unscheduled work or as a missing day.
   - Idempotent, cancellation-aware; one failing employee-day doesn't stop the rest (`PendingDayProcessor` pattern); bounded by `MaxDaysPerRun`, the remainder waits for the next night.

**Tests (P17):** no-show raised after grace and not before; day off, hourly, no-schedule, pre-hire and post-termination dates raise nothing; leave/holiday present from the start raises nothing; leave or holiday added later (through the P15b tables) closes the open no-show on re-read (`ExcusedAbsence`); removing that leave or holiday afterwards raises a new no-show; leave submitted later fixes a day's late/early minutes through the `ExcusedMinutes` comparison; accepted no-show not re-raised; late punches auto-resolve it; window-start overnight shift and window-end overnight shift; tenant-wide silent day skipped; silent device reported, active one not; hourly day Approved with no schedule comparison, no break inferred for it, missing check-out raises, cross-midnight pair accepted, an assigned schedule ignored; options (window, cap) honoured; migration applies (opt-in PostgreSQL test).

---

## Prompt 18 — Rules HR can see and change: break inference and which findings become exceptions

Depends on P15a–P17. It replaces hard-coded decisions with rows, without changing what the system does until someone edits a rule. A rule table, not a scripting language: every fact and every action is a fixed, validated list, so a rule can be shown as a plain sentence and checked before it goes live.

**Scope (assumed; overrule as needed).** Rules apply per time schedule with a tenant-wide default (`TimeScheduleId = null`), the way `AttendancePolicy` does. A rule may suppress an exception, but only with a reason, and every suppression is audited.

1. **Model** (`Module.Attendance`, `ITenantScoped`; rule sets are `ISeedData`):
   - `AttendanceRuleSet`: scope, `Version`, `Status` (`Draft`, `Published`, `Retired`), who published it and when. A published set is **never edited**; a change is a new draft, then a new published version (corrections stay additive, invariant 7).
   - `AttendanceRule`: set, `Priority`, `Name`, `IsEnabled`, `Stage` (`Interpretation`, `Finding`, `Exception`), an `Action` and its parameters.
   - `AttendanceRuleCondition`: rule, `Fact`, `Operator`, `Value`. All conditions of a rule must hold.
   - Facts (a fixed enum, all already known to the calculator): punch count, device sends break types, shift has a break, shift is overnight, gap length, gap start offset from the scheduled break, break length and excess, late and early minutes, minutes covered by leave, employee is hourly, day is excused (holiday or leave), shift has ended plus minutes.
   - Actions (a fixed enum): `InferBreak`, `IgnoreGap`, `RaiseException(type)`, `SuppressException(type, reason)`, `RequireReview`. No action creates a punch or edits a stored row; `InferBreak` retypes existing punches and marks them `Inferred` (invariants 5, 6).
2. **Evaluator** in `src/robeem.Application/Attendance/Rules`: a pure function (facts in; actions and the ids of the rules that fired out). Order: by stage, then priority; interpretation is first-match, exception is all-match, and a higher-priority `Suppress` beats a lower-priority `Raise` of the same type. `BreakInferrer` (P16) becomes the implementation of `InferBreak`; `AttendanceExceptionDetector` and the break findings read their decisions from fired rules.
3. **Defaults reproduce today.** A migration builds one **published** rule set per existing policy (and the tenant default) from the policy's own values: the break flags `RaiseExceptionOn…`, the graces, the no-break and missing-punch rules, and P16's inference parameters, and P17's no-show. A golden test runs the whole existing engine test corpus through the rules and through the old code and requires identical records and exceptions. Once it is green, the policy columns that duplicate a rule are dropped in a later migration (the `DropLegacyCatalogColumns` pattern), so there is one source of truth. The policy screen keeps the pure numbers (rounding, overtime) and links to the rules for the rest.
4. **Records say why.** `AttendanceRecord` and `AttendanceException` store the `RuleSetVersion` and the fired rule ids (part of the version comparison). The Exceptions and Attendance screens show "decided by rule X (version N)".
5. **Screens** (`RulesController`, HR administrators; nav section *Settings*):
   - the rules of a scope, each rendered as a sentence ("When a shift has a break and exactly one gap of 10–120 minutes starts within 90 minutes of it, treat it as the break (inferred)"), with enable/disable, reorder, edit and add;
   - editing works on a **draft**; **Validate** checks facts, operators and value ranges, contradictory rules, and that no action would hide something unsafe; **Publish** makes the version current;
   - **Test against a day** (employee and date): shows the facts, the rules that fire and the exceptions that would result, saving nothing (reading punches is audited as `AttendanceDataRead`);
   - **Impact preview** before publishing: how many days in the last 30 whose result would change, by exception type;
   - version history with who published what; a past version can be copied into a new draft.
6. **After publishing.** Existing days are not rewritten. P17's look-back also compares a record's `RuleSetVersion` with the current one and re-processes the stale days within the window (bounded by `MaxDaysPerRun`), so a rule change reaches recent days; older days keep the version they were decided by.
7. **Guard rails.** The payroll gate does not change: only approved, exception-free days are read (invariant 8), so a suppressed exception hides a finding from the queue but not from the record, which still shows the rule and reason. Suppression needs a reason and is audited; rules cannot reference raw data fields; the evaluator never sees a biometric field (invariant 10).

**Tests (P18):** the golden test above; each fact and action; first-match and all-match ordering; suppress vs raise priority; a draft is not live until published and a published set cannot be edited; validation refuses an unknown fact, a bad value range and a contradictory pair; the sentence rendering of every rule; the test-a-day tool and the impact preview change nothing; a rule change leads to stale days being re-processed by the look-back; rules are per tenant (isolation) and a schedule's set beats the tenant default; roles on every action; the migration builds a set equal to each existing policy (opt-in PostgreSQL test).

---

## Definition of done (all five)

Standard DoD: `dotnet build robeem.attendance.slnx` clean, `dotnet test robeem.attendance.slnx` green, `dotnet format robeem.attendance.slnx --verify-no-changes`. No raw or normalized row is edited (invariants 2, 7). No punch is invented (invariant 5). Interpretation is marked inferred (invariant 6).

## Open points to state at the end

- P18 scope was chosen by me, not by you: rules per schedule with a tenant default, and suppression of an exception allowed with a reason. Change either before P18 is written into `PROMPT_PLAN.md`.
- P18 is the largest piece. P16 and P17 are written so they work without it (the inference and exception logic stay in code until P18 moves them).
- The 2026 Lebanese holiday list is unverified (the Banque du Liban page answered 403); the lunar dates are provisional. Verify before the seed ships.
- Holiday and leave changes are not audited and are visible to HR only (P15b); a separate Leave module with approvals and balances is out of scope.
- A leave or holiday saved on a screen is picked up by the nightly look-back (P17) or the next recalculation, not instantly, because HR cannot call Attendance.
- Between P16 and P17, late-submitted leave is not re-read automatically; P17's look-back adds that.
- Per-device suppression of no-shows (employees whose own device was silent) is not done; only whole-tenant silent days are skipped.
- Existing open two-`MissingPunch` no-shows are replaced by `NoShow` the next time their day is processed.
- Work on a whole-day leave or holiday is processed normally (overtime decided by policy).
- P16 rules marked *(assumed)* need checking against the break-inference document.
- The file name still reads "P16_P17_LOOKBACK_AND_BREAK_INFERENCE" (look-back first); rename it, and its entry in `robeem.attendance.slnx`, if you want it to match the new order.

## Verification (after implementation)

Build, test, format as above. P15b: start the Api in Development, open *Holidays* on the demo tenant and see the 2026 list, add and remove one; open *Leaves*, add a whole-day and a part-day leave for an employee, change one, remove one, and check the pager keeps the filters. P15a: open *Correct a day* on a day with a break and confirm the order is enforced as check-in, break-out, break-in, check-out. P16: upload an in/out-only file with a mid-day gap on a policy with `FromGap` and check the record shows an inferred break and no break exceptions. P17: run the Api in Development; *Background jobs → Run now* for `attendance-lookback` on the demo tenant with a scheduled employee who has no punches and see the no-show on the Exceptions page; add a holiday for that date on the Holidays screen and re-run to see it close; remove the holiday and re-run to see the no-show return.
