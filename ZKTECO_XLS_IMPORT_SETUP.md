# Importing the ZKTeco U350 `.xls` export: what you configure by hand

Clock: ZKTeco U350, Fingerprint Attendance System 4.8.7 (SDK 6.2.5.5).
File: `tests/Attendance data April and May 2026.xls` (369 punches, 1 April to 27 May 2026, 4 employees).

The code change is done: the `zkteco` adapter now reads the `.xls` exactly as the software exports it, so there is no need to open it in Excel or save it as CSV. **Nothing below has been done for you.** Do it once per tenant database, in this order. Skipping a step does not lose data (every punch is stored as received); it makes rows fail or stay unresolved until you fix the step and reprocess.

## 1. Restore and restart

A new NuGet package (`ExcelDataReader` 3.7.0) was added to `robeem.Infrastructure`.

```bash
dotnet restore robeem.attendance.slnx
dotnet run --project src/robeem.Api --launch-profile http
dotnet run --project src/robeem.Web --launch-profile http   # second terminal
```

Run **one** Api instance only (`CLAUDE.md` §2.8).

## 2. Check that the `zkteco` provider is active

Restarting the Api is normally all it takes: at startup (in Development) the seed adds a missing provider and switches on one that was seeded before its adapter existed. It never renames a provider or overwrites vendor details an administrator typed.

Check: *Integrations > New*, the Provider list must offer "ZKTeco" as selectable. If it is still missing or disabled after a restart (for example outside Development, where the seed does not run at startup), run this in **each tenant database** (for example `robeem_demo`, `robeem_acme`):

```sql
UPDATE "Attendance"."AttendanceProvider" SET "IsActive" = true WHERE "ProviderKey" = 'zkteco';
```

(If the table is not found, check its name with `\dt "Attendance".*` in psql; see `DATABASE.md` §4.1.) A database created after this change gets the provider active by itself.

## 3. Create the integration

*Integrations > New*:

| Field | Value |
|---|---|
| Provider | ZKTeco |
| Name | for example `Head office U350` |
| Secret reference | leave empty (an upload has no connection secret) |
| Polling schedule | leave empty (uploads only; this adapter cannot pull from the device) |
| Status | Active |

## 4. Register the device

On the integration page, *Add device*:

| Field | Value |
|---|---|
| External device id | `1` (the file's *Location ID* column is always `1`) |
| Name | `U350` |
| Model / software / SDK | `U350` / `4.8.7` / `6.2.5.5` (optional) |
| Time zone | the clock's IANA zone, for example `Asia/Beirut`. **You must confirm this.** The file has no offset; every time is read as wall-clock time on this zone. A wrong zone shifts every punch. |

A file has no physical device, so the integration can name the device itself: on the integration's *Edit* page, choose this device under *Device for uploaded files*. Every punch of an uploaded file is then attributed to it (the device id in the file is ignored) and read in its time zone, so no punch is flagged "unrecognized device". If an earlier import was already flagged, press *Re-normalize flagged punches* on the integration page: the flagged punches are marked *Replaced* (kept as history) and normalized again.

Without a device for uploaded files, a row is read in the integration's *Time zone* (set on the integration's edit page) instead. When that is empty too, each row fails normalization ("device unknown, so its time zone is too"); set the integration's time zone or add the device, then press *Retry failed punches* on the integration page.

## 5. Make sure the employees exist and map their clock numbers

The file holds these clock numbers (*No.* column, equal to *ID Number*):

| Clock no. | Name in the file | Department |
|---|---|---|
| 21001 | Hiba Rahme | 1 - Marketing |
| 22001 | Charbel El Khoury | 2 - Sales |
| 22002 | Yara Jabbour | 2 - Sales |
| 43001 | Anthony El Khoury | 3 - Quality Control |

1. In *Employees*, create any that are missing (the employee number can be the clock number).
2. On the integration page, *Add mapping*, once per row above: external employee id = the clock number, employee = the matching HR employee. **Employees are found through these mappings only**, not by employee number. An unmapped punch is still stored, but flagged as unresolved.

## 6. Assign each employee to a time schedule

*Employees > edit*, choose a schedule (the seeded "Standard Week" is Mon to Fri 09:00 to 17:00; create your own under *Time schedules* if the real shifts differ). Without a schedule the attendance day shows "no schedule configured". The punches in the file include lunch breaks (`C/Out` around 13:00, `C/In` around 14:00), so a single 09:00 to 17:00 shift will show them as an early leave plus a re-entry; shape the schedule to the real roster if you want clean days.

## 7. Sign in with the right role

Setting up the integration, its device and mappings (steps 3 to 5) needs the `IntegrationAdministrator` role. Uploading the file (step 8) needs the `HRAdministrator` role, on the *Upload attendance* screen. In Development, `Authentication:DevelopmentUser:Enabled` already grants every role. In any other environment your identity provider must emit that role and a `tenant_id` claim. The Web has no sign-in yet (`CLAUDE.md` §2.8), so it works in Development only.

## 8. Upload

*Import > Upload attendance*: choose the integration (it fixes the format and shows its layout), pick `Attendance data April and May 2026.xls`, *Upload*. A file whose extension the integration's provider does not read is refused before it is sent. Expect:

> Uploaded Attendance data April and May 2026.xls: 369 row(s) stored, 0 unusable. Status: Succeeded.

Within about 10 seconds the normalization job turns the raw rows into events. Check them under *Raw -> normalized* on the integration page, then under *Attendance* and *Exceptions*.

## 9. What you will see, and why it is not an import fault

The clock data itself is untidy, and the system reports it instead of repairing it:

- Days with only a `C/In` or only a `C/Out` (for example 8 and 9 April for 22001) become **missing-punch exceptions** that a person resolves. A missing punch is never invented.
- Two punches seconds apart (for example 13:32:48 and 13:32:52) are marked **duplicates** at normalization; both stay stored.
- The `VerifyCode` value `Fingerpint` is the vendor's own spelling; it is kept as text only. `CardNo` is never stored.

## Limits

- The Api accepts files up to 10 MB (this one is 64 KB).
- Re-uploading the same file stores its rows again as raw rows (raw storage tolerates duplicates by design); normalization then marks the repeats as duplicates.
- Only the first sheet of a workbook is read.
