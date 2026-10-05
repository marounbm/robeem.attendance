# Attendance P15 — seed data: notes

## What Prompt 15 added

- **`AttendanceSeedContributor`** (`src/robeem.Application/Attendance/Seeding`, `Order` 200, after the HR seed), registered in `AddRobeemApplication` next to `HrSeedContributor`. `SeedDataRunner` runs it for every tenant database, as for HR. No change to the runner, the entities or the schema: `AttendanceProvider` and `AttendancePolicy` already carry `ISeedData`.
- **Providers:**

  | Key | Transport | Active |
  |---|---|---|
  | `generic-csv` | FileUpload | **yes**: the only one with an adapter |
  | `generic-webhook` | Webhook (`SupportsWebhooks`) | no |
  | `zkteco` | DeviceSdk, incremental sync | no |
  | `suprema-biostar` | RestApi, incremental sync | no |

  Display names of the inactive ones say "adapter not available yet". When an adapter is added (CLAUDE.md §1.9), flip its `IsActive` in the seed **and** note that existing tenants keep the stored value: the seed never changes a row that exists.
- **Default policy** (`TimeScheduleId` null): unlabeled punch interpretation `Off`, `RequiresOvertimePreApproval` false, correction workflow `Manager`; the other fields keep the entity defaults (120-second duplicate window, 120-minute missing-punch grace).

## Rules

- **Idempotent, and additive only.** Providers are matched by key and added when missing; the policy is added only when the tenant has no tenant-wide one. An administrator's edits, and a provider they deleted and a later run re-adds, are the only surprises: **a deleted provider comes back on the next start**.
- **No provider is seeded active without an adapter.** A test builds the real DI registration and checks that every active seeded provider has both its adapter and its event interpreter.
- `AttendanceSeedContributor.CsvProviderKey` is a literal copy of `GenericCsvProvider.ProviderKey`, because Application cannot reference Infrastructure; a test keeps them equal.

## Things to know

- **"RequiresOvertimePreApproval configurable"** is read as: the seed's value is false, and an administrator changes it on the policy row (it survives reseeding). There is no setting for the seed's own default.
- The study's vendor examples were not available; ZKTeco is the prompt's, Suprema BioStar is my pick of a realistic REST vendor. Replace freely: nothing else refers to these keys.
- **Not checked here:** the Definition of done's "after `dotnet run --project src/robeem.Api` in Development" needs the PostgreSQL databases of CLAUDE.md §5, which this session did not run. The seeding itself is covered on SQLite, with the same `SeedDataRunner`.

## Tests

`AttendanceSeedTests` (5): the catalog and policy appear once after two runs; an administrator's policy change, a renamed provider are kept and a removed one is re-added; each tenant is seeded on its own; the CSV key matches the adapter's; no active provider lacks an adapter and interpreter.
