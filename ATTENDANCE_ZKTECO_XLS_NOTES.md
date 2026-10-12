# ZKTeco `.xls` upload: notes and deviations

Goal: upload the clock's own export (`tests/Attendance data April and May 2026.xls`, U350, software 4.8.7) from the integration page, with no re-saving as CSV. What the user has to configure is in `ZKTECO_XLS_IMPORT_SETUP.md`.

## What was found

- The file is a **bare BIFF stream** (no OLE2 container, no ZIP): a BOF record `09 08 06 00`, then FONT/XF/FORMAT/COLWIDTH records and one `LABEL` (id 0x0004, BIFF2) record per cell. Every cell is text, 370 rows by 9 columns, headers `Department, Name, No., Date/Time, Status, Location ID, ID Number, VerifyCode, CardNo`.
- The existing `ZktecoProvider` and `ZktecoInterpreter` already matched those columns, the `01-Apr-26 08:33:25` timestamps and the `C/In` / `C/Out` labels. Only the file reading was missing.

## What changed

- `Attendance/Providers/TabularFile.cs` (new): reads rows of text from a CSV, an `.xlsx` or OLE2 `.xls` (through ExcelDataReader 3.7.0) or a bare BIFF stream. The format is detected from the first bytes, because the Api writes the upload to an extension-less temporary file.
- `ZktecoProvider` reads through `TabularFile`; the allowlist, interpreter, seed flags and DI are unchanged (invariants 9 and 10).
- The integration page accepts `.csv, .xls, .xlsx` and its help text no longer describes only the generic CSV.
- `ZktecoXlsUploadTests` (3), with the real file linked into the test output.

## Deviations

1. **ExcelDataReader cannot read this file.** It throws `ArgumentOutOfRangeException (index)` on the bare BIFF2 stream. `TabularFile` therefore has a small reader of its own for bare BIFF2-4 streams (text, number and integer cell records of the first sheet; no shared string table). ExcelDataReader is kept for `.xlsx` and OLE2 `.xls`, which no test exercises (no such file exists in the repository).
2. A bare BIFF5/8 stream with a shared string table is not supported by the own reader; none has been seen.
3. The seeded display name of the `zkteco` provider is now "ZKTeco" (the transport is chosen on the integration). The seed renames an existing row only when it still carries a name it seeded earlier.

4. The layout shown on the *Upload attendance* screen now describes the shared file itself: the nine columns in file order with an example each (`CardNo` marked not stored), the one timestamp format it uses (`dd-MMM-yy HH:mm:ss`) and the two labels it contains (`C/In`, `C/Out`). The interpreter still reads the other ZKTeco labels (Break In/Out, OT In/Out) and the day-first formats an Excel re-save produces; the summary says so. `Guide_ListsTheColumnsOfTheRealExport_InTheOrderTheFileHasThem` keeps the guide and the file in step.

## Pre-existing, not touched

`ApiAuthorizationTests.EveryActionOfTheApi_NamesItsRoles_ExceptTheTenantPicker` fails on `AttendanceWebhookController.Receive`, which is `[AllowAnonymous]` (it is authenticated by signature) and has no exemption in the test. Neither the controller nor the test was touched by this work (I did not run the suite on the previous commit to confirm it failed there).
