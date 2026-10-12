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
| 3 | Normalization is asynchronous | `AttendanceDataAccessArchitectureTests.IngestionAndSync_NeverCallNormalization` (new in P14, checked by a deliberate violation). The job that runs it: `AttendanceJobsTests.Normalization_*` |
| 4 | Duplicates are found only at normalization; processing trusts the result | `NormalizationServiceTests.DuplicatePair_…`, `…DuplicateWindow_ComesFromTheEmployeesSchedulePolicy`; `NormalizedAttendanceTransactionTests.*` (including a lost race); `AttendanceEndToEndTests.Scenario4_…` |
| 5 | A missing punch is never invented | Scenario 3 tests; `AttendanceProcessingEngineTests.MissingBreakPunches_…AreNotInvented_AndNeedReview` |
| 6 | Interpretation is explicit and marked | `AttendanceProcessingEngineTests.UnlabeledPunches_AreInferredSequentially_WhenThePolicySaysSo_AndMarkedInferred`; `NormalizationServiceTests.UnlabeledPunch_FailsWhenThePolicyDoesNotInterpret_AndIsKeptAsAmbiguousWhenItDoes` |
| 7 | Corrections are additive | `AttendanceCorrectionServiceTests.Scenario3_…` (version 1 kept, punch rows untouched), `…AnAppliedCorrection_SurvivesReprocessing`; every decision audited: `…ADecision_WritesAnAuditEntry_WithTheDecidersActor` and its siblings |
| 8 | Payroll reads only approved, exception-free attendance | `PayrollAttendanceExtractTests` (10); `AttendanceDataAccessArchitectureTests.*` |
| 9 | A new vendor is configuration plus one adapter | `AttendanceSeedTests.NoProviderIsSeededActive_WithoutARegisteredAdapterAndInterpreter`. `NewVendorAdditivityTests`: a second vendor, with its own formats, runs end to end with no production change |
| 10 | No biometric template is stored | `RawPayloadSanitizerTests.*`; `IngestionServiceTests.ManualUpload_StoresEveryRow_…` (the template column); `IntegrationSyncServiceTests.ASyncedPayload_NeverKeepsABiometricTemplate` (new in P14, the scheduled path) |

Tenancy, authentication and the audit trail have their own tests: `TenantIsolationTests`, `TenantRegistryTests`, `ApiAuthenticationTests`, `ApiAuthorizationTests`, `ConfigurationSecretStoreTests`, `AttendanceAuditTrailTests`, `AttendanceAuditLogTests`.

## Integration transport and custom file formats

An integration chooses how it receives attendance (`AttendanceIntegration.Transport`). `AttendanceIntegrationServiceTests` (3 new): the transport must be one the provider supports (create and update), and the polling schedule is kept only for a pulling transport. `IngestionServiceTests.UploadTargets_…` covers that only a `FileUpload` integration is an upload target (a webhook and a REST one of the same provider are not).

Manual upload is allowed for every provider, and an integration may read its uploads by a custom format. `CustomFileFormatTests` (30): format validation (required and numbered columns, delimiter, usable timestamp patterns, date + time versus one column, biometric column refused, labels known and distinct, unique name), labels kept/updated/added/dropped on change and audited, a REST-only provider given manual upload and a format (dropped when the transport changes; a retired format can't be chosen anew but is kept), the sample preview (rows as understood, nothing stored, reasons for a bad file or format, headered `|` file matched by column name), the generic layout shown for a provider with no file reader and the REST adapter never called by an upload, and a semicolon file with no header read and normalized end to end by its format (unmapped label fails that row). `FileFormatsWebTests` (6): the screens' controller over a stand-in client. Not tested: the form scripts (transport filter, add-a-label row), the HTTP client of the file formats, the Api controller's multipart `preview` over HTTP, Excel files read by a custom format (no sample workbook for the tests), and both migrations (they need the opt-in PostgreSQL tests; `AddIntegrationTransport` backfills from the provider with portable `LIKE`s).

## ZKTeco `.xls` upload

`ZktecoXlsUploadTests` (3) upload the real export of the U350 (`tests/Attendance data April and May 2026.xls`, a bare BIFF stream with text cells, copied to an extension-less temporary file as the Api does): 369 raw rows and none failed, the vendor's fields kept and `CardNo` never copied (invariant 10), and all 369 punches normalized through the mapped employees and the device in Asia/Beirut. `ZktecoAdapterTests` keep covering the CSV form. Not tested: an `.xlsx` or an OLE2 `.xls` (no such file is available to the tests), which `TabularFile` hands to ExcelDataReader.

## Raw and normalized transaction screens

Read-only lists of every raw and every normalized punch, for HR and integration administrators (`api/attendance/transactions`, the Web's *Raw transactions* and *Normalized transactions*). `AttendanceTransactionQueryTests` (9): raw punches newest first and linked to the normalized punch each became, the allowlisted payload without the card number (invariant 10), the raw filters (status, batch, integration, batch date, case-insensitive part-of-id search) and the clamped row limit, normalized punches newest punch first with employee, device and interpretation, the normalized filters (employee by number or name, event, duplicate, flag, raw id, integration, attendance date), the integration options (not audited), one `AttendanceDataRead` audit entry per list naming what was read, and another tenant's punches never showing. `TransactionsEndpointTests` (5): the three endpoints refuse Employee, Manager and Payroll (403) and anonymous (401) and admit HR and Integration; the filter reaches the service; an unknown or numeric enum value is a 400. `TransactionsWebTests` (6): the client's query strings, a 403 not swallowed, both controllers clamping rows and dropping an unknown name, and the menu items' roles. Not tested: the Razor views (no view rendering tests exist), and the screens against PostgreSQL (the lists order by `Id` or a `DateTime`, never a `DateTimeOffset`, so they should translate the same, but only the opt-in tests would show it).

## Integration time zone

A punch with no UTC offset is read in its device's zone, else in the integration's `TimeZoneId`, else it fails. `TimeZoneEdgeTests` (5 new): an unknown device with no integration zone fails with the `NormalizationFailures.NoTimeZonePrefix` wording; with an integration zone it is normalized in that zone (local time, attendance date, `TimeZoneId`) and still flagged `FlaggedUnrecognizedDevice`; a registered device's zone wins; a daylight-saving gap in the integration's zone is reported as a gap, not as a missing zone; and `RetryFailedAsync` queues only the punches that failed for want of a zone (an unparseable one stays Failed), audits it, returns null for an unknown integration, and the queued punches then normalize. `AttendanceIntegrationServiceTests` (4 new): the zone is saved trimmed, round-trips through the input and the list, blank clears it, a change is audited, and an unknown, over-long or non-IANA zone is refused. `IntegrationsEndpointTests` and `IntegrationsWebTests` (4 new): `POST …/retry-failed` is Integration-administrator only, answers the count, and is a 404 for an unknown integration; the client's request and the controller's redirect. Not tested: the form field and the *Retry failed punches* button (no view tests), and the migration `AddIntegrationTimeZone` against PostgreSQL (opt-in tests only).

## Device for uploaded files

A manual-upload (`FileUpload`) integration may name one of its own devices (`AttendanceIntegration.DefaultDeviceId`); every punch of an uploaded file is attributed to it, so none is flagged `FlaggedUnrecognizedDevice` (which the processing engine leaves out of the day). `TimeZoneEdgeTests` (3 new): the device takes every punch, an unregistered and a registered file device alike, in its zone and unflagged; another transport ignores it; `RenormalizeFlaggedAsync` takes nothing up while no device can resolve the punches, then marks the flagged punch `Excluded` (kept as history, its data untouched), sets its raw row `Pending`, and normalization adds a new unflagged punch of the device (running it again finds none; an unknown integration is null; audited), and a device registered since also qualifies. `IngestionServiceTests` (2 new): raw rows get the device and a timestamp in its zone while the file's device id stays as sent; another transport ignores it. `AttendanceIntegrationServiceTests` (2 new): the device must be one of the integration's own, is refused on create, is dropped for another transport, round-trips through the input and list and the audit text, and can't be deleted while chosen. `AttendanceTransactionQueryTests` and `IntegrationRawMappingTests` (2 new): a replaced punch is not the one its raw row links to or is shown beside. `IntegrationsEndpointTests` and `IntegrationsWebTests` (4 new): `POST …/renormalize-flagged` is Integration-administrator only, answers the count, 404s for an unknown integration; the client's request and the controller's redirect. Not tested: the form field and button (no view tests), the engine recalculating the day after a re-normalize (the pending-day processor is covered by its own tests), and the migration `AddIntegrationDefaultDevice` against PostgreSQL (opt-in tests only).

## Gaps

Real ones, in rough order of how much they matter.

1. **The study was not read.** Appendix B scenarios beyond 1–5, and any §9.2 or §14.1 row the prompts did not quote, are untested because they are unknown. Inputs of the five scenarios are reconstructed (`ATTENDANCE_P7_NOTES.md`).
2. **PostgreSQL tests exist but are opt-in, and have not been run yet.** `PostgresMigrationTests` (6) apply the migrations to a throwaway database, check that the model has no changes missing from them, run the seeds, and exercise UTC instants, the filtered unique duplicate index, intervals, the audit log, the pay-period index and tenant isolation on Npgsql. They are skipped unless `ROBEEM_TEST_POSTGRES` is set (see CLAUDE.md §5), so the default run and any CI without a server are still SQLite-only. **They were written without a server run: until someone runs them, treat their first failure as information, not as a regression.**
3. **Concurrency is simulated, not run.** `Record_ThatLosesAConcurrentRace_BecomesDuplicateConfirmed` forces the race; nothing runs two normalizations at once. CLAUDE.md §2.8 requires a single Api instance because of it.
4. ~~The scheduler is untested.~~ **Closed.** `AttendanceJobsTests` (18) cover the three jobs against faked tenancy and services: each tenant in its own scope, when an integration is due (never synced, last attempt versus last success after a restart, a failed sync waiting for its next cron time, each integration on its own cron, an invalid cron), and that a failing tenant, listing or integration never stops the others. `ScheduledJobsHostedServiceTests` (8) run the host on the real clock with millisecond intervals: interval and cron schedules, a disabled or invalid job, a job that throws keeping its schedule, runs never overlapping, a stuck job not delaying another, and a stop cancelling the run in progress. Still untested: the jobs against a real database, and the hosted service's wiring in `Program.cs`.
5. ~~No HTTP test of the ingestion upload.~~ **Closed, except the size limit.** `AttendanceIngestionEndpointTests` (10): a multipart file in and the batch outcome out, the temporary file removed (also when the service throws), an empty file 400, an unknown integration 404, a request with no file part refused, every other role 403 and nobody 401, with the service never called in the refused cases. The 10 MB limit is Kestrel's and the in-memory server does not enforce it, so it is untested.
6. ~~Time-zone edges.~~ **Closed.** `TimeZoneEdgeTests` (10), on America/New_York's 2026 changes: a time in the spring gap does not exist and its punch fails normalization with the reason (never invented); times around the gap use the offset in force; an explicit offset beats the zone; an unknown or missing zone gives no instant; the attendance date is the device's local date, not the UTC one; a device in another zone than the server counts the shift on its own clock. **One decision to confirm:** a time in the autumn overlap (01:30 happens twice) is read as *standard* time, .NET's rule, because a vendor's wall-clock time doesn't say which pass it was. A punch from the first pass is read an hour late. A vendor that sends an offset is not affected.
7. ~~Exceptions on special shifts.~~ **Closed.** `AttendanceSpecialShiftTests` (7): on an overnight shift, a missing check-out is not due until the shift's end plus the grace (the next morning), is dated on the start date, is auto-resolved when the check-out arrives, and an invalid sequence after midnight belongs to the start date; on a split shift, a missing check-out or a no-show is raised for that shift only, the day follows its weaker shift, and a punch between the shifts goes to the nearer one. No defect was found.
8. ~~Vendor additivity is asserted, not shown.~~ **Shown, with a made-up vendor.** `NewVendorAdditivityTests` (3) add "Acme Clock" (day-first timestamps, numeric event codes, a face-template field) as one adapter and one interpreter inside the test file: a day runs through sync, normalization and processing to the same 8:00 as Scenario 1, a re-delivery is absorbed, the template is stripped by the shared sanitizer, and an unknown code fails the raw row. No production code changed. **What this does not prove:** a real vendor's quirks (pagination, auth, rate limits), and that the *adapter author* allowlists fields: the sanitizer is the backstop, not a substitute.
9. **Overtime approval** (open from P11): the behaviour does not exist yet, so nothing tests it. The payroll overtime rule is tested by approving a record by hand. (Approver scope is done and tested: `AttendanceCorrectionServiceTests.AManager_SeesAndDecidesOnlyTheirOwnDirectReports` and its four siblings, `EmployeeServiceTests` for the manager field.)
10. **No Web tests for Attendance:** there are no Attendance screens. The Web API client tests cover employees only.
11. **Retention** (`RawTransactionRetentionStatus`) has no behaviour and no test.
12. **Webhook ingestion** is not built.

## Housekeeping done in P14

- `AttendanceCatalogPersistenceTests` moved to `Infrastructure/Attendance`; the audit log's append-only test moved there too (`AttendanceAuditLogTests`), as persistence behaviour.
- Added: `AttendanceEndToEndTests` (3), `AttendanceSequenceTableTests` (15), an architecture rule and a sync-path biometric test.
- Gaps 4 to 7 were closed afterwards; `TestApi` (in `Api/`) is the shared in-memory server of the HTTP tests.
