# Test coverage map

What is tested, where, and what is not. Written in P14 from the tests as they are, not from the plan.

## Read this first: the study is not in the repository

**Appendix B, §9.2 and §14.1 of the study could not be read in any prompt.** Every scenario and decision-table row below is the one the prompts quote or the one reconstructed in `ATTENDANCE_P7_NOTES.md` and `ATTENDANCE_P8_NOTES.md`. So:

- "Covered" means *covered as reconstructed*. If the study's inputs or rows differ, the tests need new data or new rows; they have not been checked against the study.
- Appendix B may hold scenarios beyond the five the prompts mention. **Those would be gaps I cannot see** (gap 1 below).

Tests live in the two folders the solution already had: `tests/robeem.Tests/Attendance` (service and engine logic) and `tests/robeem.Tests/Infrastructure/Attendance` (persistence behaviour, which needs a database). Two folders were added by earlier prompts because the layers they test fit neither: `Api` (the HTTP pipeline: authentication and role checks, P12) and `Architecture` (project and type dependency rules, P11). All tests run on in-memory SQLite.

## Appendix B worked scenarios

| # | Scenario | Test(s) |
|---|---|---|
| 1 | Standard day: 8:00 worked, 1:00 break, no late, early or overtime | `AttendanceProcessingEngineTests.Scenario1_StandardDay` (the numbers); `AttendanceEndToEndTests.Scenario1_AFileOfAStandardDay_ComesOutAsEightHours_WithTheDeviceDoubleTapAbsorbed` (the same day from a CSV file, through ingestion and normalization) |
| 2 | Late arrival with overtime: 12 min late, 1:23 overtime, PendingReview under pre-approval | `AttendanceProcessingEngineTests.Scenario2_LateArrivalWithOvertime_NeedsReviewWhenOvertimeRequiresPreApproval`; `…Scenario2_WithoutPreApprovalRequirement_IsApproved`; payroll side: `PayrollAttendanceExtractTests.OvertimeNeedingPreApproval_IsExcluded_UntilTheCorrectionExists` |
| 3 | Missing break-out, then corrected to 12:00 | Detection: `AttendanceExceptionDetectionTests.Scenario3_MissingBreakOut_RaisesOneOpenException_AndTheRecordNeedsReview`. Correction end to end: `AttendanceCorrectionServiceTests.Scenario3_ProposeThenManagerApproves_RecalculatesTheDay_AndClosesTheException`; survives reprocessing: `…AnAppliedCorrection_SurvivesReprocessing` |
| 4 | Duplicate from an integration retry never contributes hours | **New in P14**, ingestion → normalization → processing: `AttendanceEndToEndTests.Scenario4_AnIntegrationRetryThatDeliversTheFileAgain_NeverContributesHours` (10 raw rows stored, 4 Unique, 6 DuplicateConfirmed, 8:00 worked once, no duplicate among the record's sources) and `…Scenario4_ReprocessingAfterTheRetry_ChangesNothing`. Scheduled-sync path: `IntegrationSyncServiceTests.RetriedSyncRedeliveringOverlap_LandsAsDuplicates_AndCountsTheHoursOnce`. Building blocks: `NormalizationServiceTests.DuplicatePair_GivesOneUniqueAndOneDuplicateConfirmed`, `NormalizedAttendanceTransactionTests.Record_OfTheSameKeyTwice_StoresOneUniqueAndOneDuplicateConfirmed` |
| 5 | Overnight shift: dated on its start, worked across midnight | `AttendanceProcessingEngineTests.Scenario5_OvernightShift_IsDatedOnItsStart_AndWorkedAcrossMidnight`; resolution: `ScheduleResolutionServiceTests.OvernightShift_IsOvernight_AndEndsTheNextDay`, `…IsOvernight_ComparesEndWithStart` |

## §9.2: when a missing punch is an exception

Rows as reconstructed in `ATTENDANCE_P8_NOTES.md`.

| Row | Test(s) |
|---|---|
| Check-out with no check-in: raised at once | `AttendanceExceptionDetectionTests.MissingCheckIn_ACheckOutWithNoCheckIn` |
| Check-in with no check-out: not before the grace window | `…MissingCheckOut_IsNotRaisedBeforeTheGraceWindowHasElapsed` |
| … raised once the grace has elapsed, and only once | `…MissingCheckOut_IsRaisedOnceTheGraceWindowHasElapsed_AndOnlyOnce` |
| … the grace comes from the policy | `…MissingCheckOut_UsesTheGraceOfThePolicy` |
| Break-in with no break-out (shift has a break) | `…Scenario3_MissingBreakOut_…` |
| Break-out with no break-in | `…MissingBreakIn_ABreakOutWithNoBreakIn` |
| … while the break may still be running: waits for the grace | `…MissingBreakIn_WhileTheBreakMayStillBeRunning_WaitsForTheGrace` |
| Shift with a break, worked with no break punch at all | `…MissingBreakPunches_OnAShiftWithABreak_WorkedWithoutAnyBreakPunch`; `AttendanceProcessingEngineTests.MissingBreakPunches_OnAShiftWithABreak_AreNotInvented_AndNeedReview` |
| Scheduled day with no punch at all (no-show), after the grace | `…AScheduledDay_WithNoPunchesAtAll_IsAMissingCheckInAndCheckOut_OnceTheGraceHasElapsed` |
| Day off: nothing expected, nothing raised | `…ADayOff_WithNoPunches_RaisesNoException_EvenLongAfterwards` |
| No schedule configured: nothing raised | `…AnEmployeeWithoutASchedule_WithNoPunches_RaisesNoException`; `ScheduleResolutionServiceTests.EmployeeWithoutAssignment_HasNoScheduleConfigured` |
| Work on a day off: a record with no shift, for review | `AttendanceProcessingEngineTests.WorkOnADayOff_IsRecordedWithoutAShift_ForReview` |
| Reprocessing: an open exception whose punch arrives late is auto-resolved | `…AnOpenException_IsAutoResolved_WhenTheMissingPunchArrivesLate` |
| Reprocessing: a PendingCorrection exception is left alone | `…APendingCorrectionException_IsLeftAlone_WhateverReprocessingFinds` |
| An open exception takes an approval back | `…AnApprovalIsTakenBack_WhenAnOpenExceptionExists_AndTheCalculationIsUnchanged` |

## §14.1: punches in the wrong order

One row per state-and-punch pair of `AttendanceExceptionDetector`. The table rows added in P14 are `AttendanceSequenceTableTests`; before it, only four of them had a test.

| Row | Test(s) |
|---|---|
| Check-out before the first check-in | `AttendanceExceptionDetectionTests.InvalidSequence_ACheckOutBeforeTheCheckIn`; `AttendanceSequenceTableTests.OnAShiftWithABreak_…` (case "a check-out before the first check-in") |
| Check-in after a check-in, inside the duplicate window: one punch | `…InvalidSequence_TwoCheckInsInsideTheDuplicateWindow_AreOnePunch` |
| … outside the window: invalid | `…InvalidSequence_TwoCheckInsOutsideTheDuplicateWindow`; table case "a check-in after a check-in, outside the window" |
| Repeat inside the window, for break-out, break-in and check-out too | `AttendanceSequenceTableTests.APunchRepeatedInsideTheDuplicateWindow_IsOnePunch_OfEveryType` |
| Check-in during a break | table case "a check-in during a break" |
| Break-out after a break-out | table case "a break-out after a break-out" |
| Break-out before the first check-in, or after the check-out | two table cases |
| Break-in before the first check-in, or after the check-out | two table cases |
| Check-out after a check-out | table case "a check-out after a check-out" |
| Break-in with no break-out, shift without a break: invalid | `…InvalidSequence_ABreakInWithNoBreakOut_OnAShiftWithoutABreak` |
| Break-out nothing closed, shift without a break: invalid, not a missing break-in | `AttendanceSequenceTableTests.OnAShiftWithoutABreak_ABreakOutNothingClosed_IsAnInvalidSequence_NotAMissingBreakIn` |
| An invalid sequence keeps the day from being approved; the rest of the day still calculates | `…InvalidSequence_ACheckOutBeforeTheCheckIn`; every table case |
| An invalid sequence can not be corrected by adding a punch | `AttendanceCorrectionServiceTests.AnInvalidSequence_CannotBeCorrectedByAddingAPunch` |
| A clean day raises nothing | `AttendanceSequenceTableTests.ACleanDay_RaisesNothing`; `AttendanceProcessingEngineTests.Scenario1_StandardDay` |

## Architecture invariants (CLAUDE.md §1)

| # | Invariant | Test(s) |
|---|---|---|
| 1 | Every raw transaction is stored | `IngestionServiceTests.ManualUpload_StoresEveryRow_AndCountsTheMalformedOneAsFailed`; `AttendanceEndToEndTests.Scenario4_…` (duplicates stored too) |
| 2 | Raw data is immutable | `RawAttendanceTransactionTests`: `Update_OfAVendorField_Throws`, `Update_OfADetachedRow_Throws`, `Delete_Throws`, and the allowed bookkeeping update |
| 3 | Normalization is asynchronous | `AttendanceDataAccessArchitectureTests.IngestionAndSync_NeverCallNormalization` (new in P14, checked by a deliberate violation). **The job that runs it is not tested** (gap 4) |
| 4 | Duplicates are found only at normalization; processing trusts the result | `NormalizationServiceTests.DuplicatePair_…`, `…DuplicateWindow_ComesFromTheEmployeesSchedulePolicy`; `NormalizedAttendanceTransactionTests.*` (including a lost race); `AttendanceEndToEndTests.Scenario4_…` |
| 5 | A missing punch is never invented | Scenario 3 tests; `AttendanceProcessingEngineTests.MissingBreakPunches_…AreNotInvented_AndNeedReview` |
| 6 | Interpretation is explicit and marked | `AttendanceProcessingEngineTests.UnlabeledPunches_AreInferredSequentially_WhenThePolicySaysSo_AndMarkedInferred`; `NormalizationServiceTests.UnlabeledPunch_FailsWhenThePolicyDoesNotInterpret_AndIsKeptAsAmbiguousWhenItDoes` |
| 7 | Corrections are additive | `AttendanceCorrectionServiceTests.Scenario3_…` (version 1 kept, punch rows untouched), `…AnAppliedCorrection_SurvivesReprocessing`; every decision audited: `…ADecision_WritesAnAuditEntry_WithTheDecidersActor` and its siblings |
| 8 | Payroll reads only approved, exception-free attendance | `PayrollAttendanceExtractTests` (10); `AttendanceDataAccessArchitectureTests.*` |
| 9 | A new vendor is configuration plus one adapter | `AttendanceSeedTests.NoProviderIsSeededActive_WithoutARegisteredAdapterAndInterpreter`. **Not demonstrated**: only one adapter exists (gap 8) |
| 10 | No biometric template is stored | `RawPayloadSanitizerTests.*`; `IngestionServiceTests.ManualUpload_StoresEveryRow_…` (the template column); `IntegrationSyncServiceTests.ASyncedPayload_NeverKeepsABiometricTemplate` (new in P14, the scheduled path) |

Tenancy, authentication and the audit trail have their own tests: `TenantIsolationTests`, `TenantRegistryTests`, `ApiAuthenticationTests`, `ApiAuthorizationTests`, `ConfigurationSecretStoreTests`, `AttendanceAuditTrailTests`, `AttendanceAuditLogTests`.

## Gaps

Real ones, in rough order of how much they matter.

1. **The study was not read.** Appendix B scenarios beyond 1–5, and any §9.2 or §14.1 row the prompts did not quote, are untested because they are unknown. Inputs of the five scenarios are reconstructed (`ATTENDANCE_P7_NOTES.md`).
2. **PostgreSQL is never exercised.** All tests use SQLite. No test applies the migrations (`EnsureCreated` builds the schema from the model), so a migration that differs from the model, a Npgsql type mapping, the `timestamp without time zone` rule or the filtered unique indexes on PostgreSQL could fail unseen. The SQLite `UtcDateTimeGuardInterceptor` mimics one of those rules only.
3. **Concurrency is simulated, not run.** `Record_ThatLosesAConcurrentRace_BecomesDuplicateConfirmed` forces the race; nothing runs two normalizations at once. CLAUDE.md §2.8 requires a single Api instance because of it.
4. **The scheduler is untested.** `ScheduledJobsHostedService`, `NormalizationJob`, `IntegrationSyncJob` (including its `IsDue` rule: last attempt versus last success) and `ReconciliationJob` have no tests; only the services they call, and `CronSchedule`, do. Nor is "one tenant failing does not stop the others".
5. **No HTTP test of the ingestion upload** (multipart, size limit). Authorization of the endpoint is covered by `ApiAuthorizationTests.EveryActionOfTheApi_NamesItsRoles_ExceptTheTenantPicker`, its behaviour only through the service.
6. **Time-zone edges:** a punch in a daylight-saving gap or overlap, and a device whose zone differs from the server's across midnight, have no test of their own.
7. **Exceptions on special shifts:** a missing punch on an overnight shift, on a split shift, or across two shifts of one day is untested; Scenario 5 covers only the calculation.
8. **Vendor additivity (invariant 9) is asserted, not shown:** with one adapter there is no second one to prove that adding it touches nothing else. Likewise only the CSV adapter's field allowlist is tested.
9. **Approver scope and overtime approval** (open from P9 and P11): their behaviour does not exist yet, so nothing tests it. The payroll overtime rule is tested by approving a record by hand.
10. **No Web tests for Attendance:** there are no Attendance screens. The Web API client tests cover employees only.
11. **Retention** (`RawTransactionRetentionStatus`) has no behaviour and no test.
12. **Webhook ingestion** is not built.

## Housekeeping done in P14

- `AttendanceCatalogPersistenceTests` moved to `Infrastructure/Attendance`; the audit log's append-only test moved there too (`AttendanceAuditLogTests`), as persistence behaviour.
- Added: `AttendanceEndToEndTests` (3), `AttendanceSequenceTableTests` (15), an architecture rule and a sync-path biometric test.
