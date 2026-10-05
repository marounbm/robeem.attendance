# Attendance P13 — observability: notes

## What Prompt 13 added

- **`IIntegrationHealthService.GetIntegrationHealthAsync(integrationId, ct)`** in `src/robeem.Application/Attendance`, returning an `IntegrationHealthReport` (in `robeem.Contracts/Attendance`; strings for the enums, so Web can use it). Null for an unknown integration. Read-only.
- **Endpoint:** `GET api/attendance/integrations/{id}/health` (`AttendanceIntegrationsController`). It needs the Integration Administrator role in P12 (`TODO(P12)`).
- **Option:** `Attendance:Sync:HealthWindowHours` (default 24), the window the KPIs cover.
- **No new schema, and no metrics package.** All six KPIs come from existing tables; plain `ILogger` is what the sync job already uses. (`dotnet ef migrations` was not run for this prompt.)
- `IntegrationSyncService` now stamps its batches' `StartedAt` from the injected clock (it was `DateTime.Now`), so the window is testable.

## The report

Stored state: `HealthStatus` (kept by the sync job, P10), `Status`, `LastSuccessfulSyncAt`, the last failed sync (time and reason), and `IsCircuitOpen` from `IntegrationResilience` (in memory: a restart closes it, but `HealthStatus` persists).

| KPI | Definition |
|---|---|
| Sync success rate | of the `IncrementalSync` batches that started in the window, the share that did not fail. A failed sync is a `Failed` batch with a reason and no rows. |
| Failed transaction rate | raw rows of the window with `ProcessingStatus = Failed`, over all raw rows of the window. |
| Duplicate rate | normalized rows of the window with `DuplicateConfirmed`, over all normalized rows of the window. |
| Missing event rate | `MissingPunch` exceptions, over employee-days with a Unique punch from this integration. |
| Average sync delay | mean of (received − punch time) over the window's raw rows that have a time, **uploaded files left out** (a file is history by design and would swamp it); never negative. |
| Unresolved exceptions | exceptions not `Closed`. |

A rate is `null` when nothing in the window measures it, never 0.

## Please look at: decisions I had to make (the study's §17.3 was not available)

- **The sync rate was biased low, and no longer is.** Originally a sync that delivered nothing wrote no batch, so a quiet integration with one failure a day reported a poor rate. Now every successful sync leaves a batch (a 0-record `Succeeded` one when it delivered nothing), and the nightly reconciliation job prunes the empty ones older than `Attendance:Sync:EmptySyncRetentionDays` (default 7; 0 keeps all). About 288 rows a day per integration at a five-minute schedule, so about 2,000 are held at any time. The prune never touches a failed batch, one with raw rows, or an upload. **The rate is meaningful only over windows within the retention** (the default health window is 24 hours).
- **The window of a transaction is its batch's `StartedAt`**, not `RecordReceivedAt`: SQLite, which the tests run on, cannot compare `DateTimeOffset`. They coincide in practice.
- **Exceptions have no integration.** They belong to an employee and a date, so an exception counts for an integration that delivered a punch of that employee on that date. A day with no punches at all (a full absence) belongs to no integration; it shows only in the tenant's own exception list.
- **The report reads; it does not set `HealthStatus`.** The sync job owns that. "Backing `HealthStatus`" is met by showing it with the KPIs behind it. Thresholds for turning a bad KPI into `Degraded` are a business decision I did not invent.
- **Nothing is emitted yet** (no counters, no alerting): the health query is the contract. Study §17.3's alerts, if any, need that decision and a metrics package.

## Tests

`IntegrationHealthKpiTests` (the six KPIs over seeded data, with rows outside the window, an uploaded file, a closed exception and a day with no punches left out; and null rates for an empty window) and, in `IntegrationSyncServiceTests`, `HealthReport_ReflectsTheTrippedBreaker`: after two failed syncs the report says `Unhealthy`, the breaker open, a 0% sync rate and the last failure's reason.
