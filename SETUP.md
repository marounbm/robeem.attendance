# robeem Attendance: setup and configuration guide

How to get the solution running on a development machine, how to configure it for a real deployment, and how to bring in the first punches. The design rules behind these settings are in `CLAUDE.md` (§2.8 for tenancy, authentication and background jobs).

The solution has two processes:

| Process | Project | Default URL | Role |
|---|---|---|---|
| Api | `src/robeem.Api` | http://localhost:5181 | The only process that touches a database. Runs the background jobs. |
| Web | `src/robeem.Web` | http://localhost:5180 | MVC front end. Reaches data only through the Api over HTTP. |

---

## 1. Prerequisites

| Requirement | Notes |
|---|---|
| .NET SDK 10 | `dotnet --version` should print `10.x`. |
| `dotnet-ef` 10 | `dotnet tool install -g dotnet-ef`. Needed only to add migrations. |
| PostgreSQL server | Development uses `localhost:5432`, user `postgres`. The user must be allowed to create databases. |
| Git | |

Each tenant is a separate PostgreSQL **database**. The Api creates a missing database and applies the migrations on start (section 4), so you do not create any tables by hand.

---

## 2. Get the code and build

From the repository root:

```bash
dotnet build robeem.attendance.slnx
```

A clean build has zero errors and zero warnings. If it fails on a StyleCop rule, fix the code; do not suppress the rule (`CLAUDE.md` §7).

---

## 3. Configure the database connections (Development)

`src/robeem.Api/appsettings.Development.json` lists two tenants, `demo` and `acme`, with **password-less** connection strings. Passwords are never committed. Put the full strings in the Api's user-secrets (its `UserSecretsId` is `robeem-attendance-api`); they override the file by array index:

```bash
dotnet user-secrets set "Tenancy:Tenants:0:ConnectionString" "Host=localhost;Database=robeem_demo;Username=postgres;Password=<pwd>" --project src/robeem.Api
dotnet user-secrets set "Tenancy:Tenants:1:ConnectionString" "Host=localhost;Database=robeem_acme;Username=postgres;Password=<pwd>" --project src/robeem.Api
```

Index 0 is `demo`, index 1 is `acme`: the order in `appsettings.Development.json`. If you change that order, change the indexes.

Check what is stored: `dotnet user-secrets list --project src/robeem.Api`.

The Web needs nothing secret. In Development, `src/robeem.Web/appsettings.Development.json` already points it at the Api (`RobeemApi:BaseUrl` = `http://localhost:5181/`).

---

## 4. Run the Api

```bash
dotnet run --project src/robeem.Api --launch-profile http
```

In Development, `Database:InitializeOnStartup` is `true`, so before it starts listening the Api, for every configured tenant:

1. creates the PostgreSQL database if it is missing;
2. applies all migrations;
3. runs the seeders, which add only what is missing:
   - the HR seed: a "Standard Week" time schedule (Mon to Fri, 09:00 to 17:00) with its all-inclusive team;
   - the Attendance seed: the provider catalog (`generic-csv` active; `generic-webhook`, `zkteco` and `suprema-biostar` listed but inactive because they have no adapter yet) and the tenant-wide default attendance policy.

The same Api process also runs the background jobs (section 8): normalization, integration sync and the nightly reconciliation.

**Run exactly one Api instance.** There is no distributed lock (`CLAUDE.md` §2.8).

Quick check: `GET http://localhost:5181/api/tenants` returns the tenant list and needs no sign-in.

---

## 5. Run the Web

In a second terminal, with the Api running:

```bash
dotnet run --project src/robeem.Web --launch-profile http
```

Open http://localhost:5180.

1. **Pick a tenant** on the `/Tenants` page (`demo` or `acme`). The choice is kept in the `robeem.tenant` cookie and sent to the Api as the `X-Tenant-Id` header.
2. **"Act as"** (Development only): the Web has no sign-in yet, so a switcher lets you choose which employee and which roles you are using. The menu shows only the screens that the chosen roles may open; the Api enforces the roles regardless.

### The five roles

| Role | Can |
|---|---|
| `Employee` | See their own days and exceptions; propose a correction for a missing punch. |
| `Manager` | The same for their direct reports; decide corrections and reviews of their reports. |
| `HRAdministrator` | Employees, schedules, policies, the audit log; everyone's attendance; HR decisions; "Calculate again". |
| `PayrollAdministrator` | Pay periods and the approved-days payroll view. |
| `IntegrationAdministrator` | Integrations, devices, employee-id mappings, uploads and sync. |

---

## 6. First-time walk-through (the first punches)

Do this once per tenant, as a user with the roles shown.

0. **Define the time schedules** (`HRAdministrator`, *People → Time schedules*). Every tenant starts with "Standard Week" (Monday to Friday, 09:00 to 17:00, 13:00 to 14:00 break). To add your own: create the schedule (its name and how long the pattern is: 7 days for a week, or a longer rotation with the date of its day 1), add its **shifts**, then tick the shifts each day of the pattern works in the **roster**. A day with nothing ticked is a day off; two shifts on one day is a split shift. Every day is calculated against the schedule its employee is assigned to, and changes apply to days processed from then on.
1. **Create employees** (`HRAdministrator`, *Employees*).
   - Give each a unique employee number.
   - Assign a time schedule. The employee joins that schedule's default team. An employee without a schedule gets a "no schedule configured" result rather than a guessed one.
   - Optionally set a manager. A manager sees and decides only their direct reports; HR covers employees with no (or an inactive) manager.
2. **Check the attendance policy** (`HRAdministrator`, *Policies*). The seeded default policy applies to everyone. A schedule can have its own policy. A change applies to days processed from then on.
3. **Create an integration** (`IntegrationAdministrator`, *Integrations*).
   - Choose a provider. Only a provider with an adapter can be chosen: today that is **Generic CSV upload**.
   - *Secret reference*: the *name* a secret is kept under in configuration (letters, digits and `. _ : / -`). Never type the secret itself. Leave it empty for an integration you only upload files to (section 7).
   - *Schedule*: a cron expression of five fields (minute hour day month weekday; for example `*/5 * * * *`) or six with seconds first, or empty for uploads only.
4. **Add the devices** on the integration's page. For each: the id as the vendor reports it, a name, the type, and the **IANA time zone of the device's clock** (for example `Asia/Beirut`). A punch without a UTC offset is read in its device's zone. Model, software version and SDK version are optional and are for your records.
5. **Map vendor employee ids to employees** on the integration's page. A punch whose vendor id has no mapping cannot be assigned to an employee.
6. **Upload a file**. For Generic CSV, an RFC 4180 CSV with a header row and columns such as `transaction_id, employee_id, timestamp, type, device_id`. The accepted header names are listed in `src/robeem.Infrastructure/Attendance/Providers/GenericCsvProvider.cs`.
7. **Wait a few seconds.** Ingestion stores every punch unchanged (raw data is never edited). The normalization job (every 10 seconds by default) then turns raw punches into events, resolves the employee and device, and marks duplicates. The processing engine then calculates the day.
8. **Look at the result**: *Attendance* (one line per employee and day, with the full working-out when you open a day) and *Exceptions* (missing punches and wrong order, which a person has to settle).

---

## 7. Connecting a real time clock

- **Generic CSV (available now):** upload files as in step 6.
- **A provider with an API or device protocol:** needs its adapter and event interpreter in `src/robeem.Infrastructure/Attendance/Providers`, both registered as keyed services under the provider's key, and the provider's `IsActive` flag switched on in `AttendanceSeedContributor` (`CLAUDE.md` §1.9, §4, and `ATTENDANCE_P15_NOTES.md`). Existing tenants keep the stored value of `IsActive`, because the seed never changes a row that exists; change it in the tenant database or through the catalog.
- **ZKTeco** (for example the U350 with *Fingerprint Attendance System* 4.8.7 and SDK 6.2.5.5): the vendor and the device fields are recorded, but there is no adapter yet, so the provider is inactive. An adapter needs a sample export from the machine or a decision on how punches are pulled.
- **Secrets for a provider connection:** the integration stores only a *reference*. The Api resolves it to the secret at `Secrets:{tenantId}:{reference}` in configuration for a single call, and never logs or stores it.

  ```bash
  # Development
  dotnet user-secrets set "Secrets:demo:acme-clock-api" "<the secret>" --project src/robeem.Api
  ```

  In production use environment variables (`Secrets__demo__acme-clock-api`) or any configuration provider, such as Azure Key Vault (secret name `Secrets--demo--acme-clock-api`). Nothing in the code changes.

---

## 8. Configuration reference

All settings are in `src/robeem.Api/appsettings.json`, overridden per environment. In environment variables a `:` becomes `__`.

### Tenancy and database

| Setting | Default | Meaning |
|---|---|---|
| `Tenancy:Tenants` | `[]` | The tenants: `Id` (lower-case, at most 64), `Name`, `ConnectionString`. Validated at startup; bad configuration stops the Api. |
| `Tenancy:AllowUnauthenticatedTenantSelection` | `false` | When true, a request without a token may choose its tenant with the `X-Tenant-Id` header, else `DefaultTenantId`. **Development only.** |
| `Tenancy:DefaultTenantId` | none | Used by the setting above. |
| `Database:InitializeOnStartup` | `false` (`true` in Development) | Create, migrate and seed every tenant database at startup. |

### Authentication

| Setting | Default | Meaning |
|---|---|---|
| `Authentication:Jwt:Authority` | empty | The identity provider, for example `https://login.microsoftonline.com/{tenant}/v2.0` for Entra ID. Any OpenID Connect provider works. |
| `Authentication:Jwt:Audience` | empty | The audience the tokens are issued for: this Api's application id or URI. |
| `Authentication:DevelopmentUser:Enabled` | `false` (`true` in Development) | Authenticates every request without a token as a stand-in user. **The Api refuses to start with this on outside Development.** |
| `Authentication:DevelopmentUser:EmployeeId` | none | The `Profile` id of the stand-in user. |
| `Authentication:DevelopmentUser:Roles` | all five | The stand-in user's roles. |

The Api only validates tokens; it has no user store and issues none. The identity provider must put these claims in the token:

| Claim | Meaning |
|---|---|
| `tenant_id` | The tenant database. Must name a configured tenant. |
| `role` (one per role) | `Employee`, `Manager`, `HRAdministrator`, `PayrollAdministrator`, `IntegrationAdministrator`. |
| `employee_id` | The user's `Profile` id: who proposes or decides a correction, and the actor in the audit log. |

### Background jobs

| Setting | Default | Meaning |
|---|---|---|
| `Attendance:Normalization:Enabled` | `true` | Run the normalization job. |
| `Attendance:Normalization:PollIntervalSeconds` | `10` | How often it looks for new raw punches. |
| `Attendance:Sync:Enabled` | `true` | Run the integration sync job. |
| `Attendance:Sync:TickSeconds` | `15` | How often it checks which integrations are due by their own cron. |
| `Attendance:Sync:OverlapMinutes` | `5` | Each incremental sync re-reads this much before its cursor; duplicates are absorbed by normalization. |
| `Attendance:Sync:RetryAttempts` | `3` | Retries of a provider call, with exponential backoff and jitter. |
| `Attendance:Sync:RetryDelaySeconds` | `2` | Base delay of the backoff. |
| `Attendance:Sync:BreakerFailureThreshold` | `3` | Failures in the sampling window that open the circuit breaker. |
| `Attendance:Sync:BreakerSamplingMinutes` | `60` | The sampling window. |
| `Attendance:Sync:BreakerBreakSeconds` | `300` | How long the breaker stays open. |
| `Attendance:Sync:ReconciliationEnabled` | `true` | Run the nightly reconciliation. |
| `Attendance:Sync:ReconciliationCron` | `0 2 * * *` | When it runs. |
| `Attendance:Sync:ReconciliationWindowHours` | `48` | How far back it re-reads. |
| `Attendance:Sync:EmptySyncRetentionDays` | `7` | Batches of syncs that found nothing are pruned after this long. |

### Web

| Setting | Meaning |
|---|---|
| `RobeemApi:BaseUrl` | The Api's absolute http(s) URL. Validated at startup; the Web will not start without it. |

---

## 9. Configuring a deployment

Do **not** copy the Development settings.

1. **Tenants.** Supply each tenant's connection string through environment variables or a secret store, never a committed file:

   ```
   Tenancy__Tenants__0__Id=acme
   Tenancy__Tenants__0__Name=Acme Corporation
   Tenancy__Tenants__0__ConnectionString=Host=...;Database=robeem_acme;Username=...;Password=...
   ```

2. **Authentication.** Set `Authentication__Jwt__Authority` and `Authentication__Jwt__Audience`, and configure the identity provider to emit `tenant_id`, `role` and `employee_id`.
3. **Leave off** `Tenancy:AllowUnauthenticatedTenantSelection` and `Authentication:DevelopmentUser:Enabled`. The Api refuses to start if either is on outside Development.
4. **Database creation.** `Database:InitializeOnStartup` creates databases and applies migrations. Set it to `true` for the first start (or use a deployment step with an account allowed to create databases), then decide whether you want migrations applied on every start.
5. **Secrets** for provider connections: see section 7.
6. **One Api instance** (section 4). To scale out, `CLAUDE.md` §2.8 describes what to adopt instead.
7. **Web sign-in is not built.** The Web has no login and does not forward tokens, so in production its calls to the Api are answered 401. Until OpenID Connect sign-in is added to the Web, it works in Development only.

---

## 10. Migrations

One schema serves all tenants; each tenant database is migrated at startup.

```bash
dotnet ef migrations add <MigrationName> --project src/robeem.Infrastructure --startup-project src/robeem.Api --output-dir Persistence/Migrations
```

There is no `dotnet ef database update` step. `dotnet ef` builds the model with a placeholder tenant; set `ROBEEM_DESIGN_CONNECTION` to change the connection string it uses.

---

## 11. Verify the installation

```bash
dotnet build robeem.attendance.slnx                      # zero errors, zero warnings
dotnet test robeem.attendance.slnx                       # in-memory SQLite; PostgreSQL tests are skipped
dotnet format robeem.attendance.slnx --verify-no-changes # formatting
```

C# files use CRLF line endings. If a tool wrote LF, run `dotnet format robeem.attendance.slnx whitespace`.

The opt-in PostgreSQL tests apply the migrations to a throwaway database. Use an account that may create databases; each run creates and drops its own `robeem_migtest_*` database. Never commit the password.

```powershell
$env:ROBEEM_TEST_POSTGRES = "Host=localhost;Username=postgres;Password=<pwd>"
dotnet test robeem.attendance.slnx --filter "FullyQualifiedName~PostgresMigrationTests"
```

```bash
export ROBEEM_TEST_POSTGRES="Host=localhost;Username=postgres;Password=<pwd>"
```

---

## 12. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| Api exits at startup naming `Tenancy` | A tenant is missing an id, name or connection string, or ids repeat. Fix `Tenancy:Tenants`. |
| Api exits at startup naming `DevelopmentUser` or `AllowUnauthenticatedTenantSelection` | One is on outside Development. Turn it off. |
| Connection or password error at startup | The user-secret connection strings are missing or their indexes do not match the tenant order (section 3). |
| Web fails at startup on `RobeemApi:BaseUrl` | Set it to the Api's absolute URL. |
| Web redirects to the tenant picker | No tenant chosen, or the cookie names a tenant the Api does not know. Pick one. |
| 403 "tenant_required", 400 "unknown_tenant", 403 "tenant_unavailable" | The request has no usable tenant. Pick a tenant in the Web, send `X-Tenant-Id` (Development), or fix the token's `tenant_id` claim. |
| 403 "Your account is not linked to an employee" | The user has no `employee_id`. In Development, choose an employee in "act as", or set `Authentication:DevelopmentUser:EmployeeId`. |
| 401 from the Web in production | Expected: the Web has no sign-in yet (section 9). |
| Employee and device appear in the data but no day is calculated | The vendor employee id has no mapping (section 6, step 5), or the employee has no schedule. Check *Exceptions* and the integration page. |
| A punch time is hours off | The device's time zone is wrong. Fix it on the device (section 6, step 4); times without a UTC offset are read in it. |
| The provider cannot be chosen on a new integration | It has no adapter yet (section 7). |
| A rejected punch or duplicate | Raw punches are always kept; duplicates are marked at normalization, not deleted. Open the day to see each punch. |
