# Attendance P12 — authentication, roles, secrets, audit log: notes

## The decision is provisional

The prompt requires a human to choose the authentication approach. I asked, and was told to continue without an answer, so I chose the recommendation and recorded it in CLAUDE.md §2.8, marked **provisional**: **JWT bearer validation in the Api against an external identity provider, Entra ID as the target**. The Api is a resource server only: no user store, no token issuing. The reasons and the rejected options (Identity with cookies, Api-issued JWTs) are in §2.8. Changing it later means replacing `AddRobeemAuthentication`; roles, claims, policies and the audit log do not change.

## What Prompt 12 added

- **Authentication** (`src/robeem.Api/Security`): `AddRobeemAuthentication` (JwtBearer: `Authentication:Jwt:Authority` and `:Audience`, claims keep their token names, `role` is the role claim), `HttpCurrentUser`, `AttendanceRoles`, `SecurityClaims`. `UseAuthentication()` now runs before `TenantResolutionMiddleware`, whose contract is unchanged: it already read `tenant_id`.
- **The token must carry** `tenant_id`, one `role` claim per role, and `employee_id` (the `Profile` id). Configuring the identity provider to emit these (in Entra: app roles, plus an optional claim or claims mapping for the other two) is a task outside this repository.
- **Startup guards:** outside Development the Api refuses to start without an Authority and Audience, or with the stand-in user on.
- **Roles on every endpoint** (`[Authorize(Roles = ...)]`):

  | Endpoint | Role |
  |---|---|
  | `POST corrections` (propose) | Employee, and the token has an `employee_id` |
  | `GET corrections/pending` | Manager for `awaiting=Manager`, HRAdministrator for `awaiting=HR` |
  | `POST corrections/{id}/decision` | Manager or HRAdministrator |
  | `POST ingestion/{id}/uploads`, `GET integrations/{id}/health` | IntegrationAdministrator |
  | `GET payroll/approved?from&to` (**new**) | PayrollAdministrator |
  | `GET audit` (**new**) | HRAdministrator |
  | `api/employees`, `api/schedules` (HR screens) | HRAdministrator |
  | `api/tenants` | none (the tenant picker) |

  A test fails if a new action has no roles. Requester and decider are the authenticated user; `RequestedByEmployeeId` and `DecidedByUserId` are **gone from the request bodies**. A user with one approver role decides in it; a user with both says which with `IsHrDecision`.
- **Payroll endpoint:** `PayrollAttendanceDay` and `PayrollAttendanceShift` moved to `robeem.Contracts` so the Api returns Contracts DTOs, not Application types.
- **Secrets:** `ISecretStore` (Application) and `ConfigurationSecretStore` (Infrastructure) resolve `ConnectionConfigRef` to the secret under `Secrets:{tenantId}:{reference}`. The sync service hands it to the adapter as `AttendanceIntegrationConfig.ConnectionSecret` for one call (retried with the call; kept out of the config's `ToString`). Development: user-secrets. Production: environment variables or a vault added as a configuration provider (Azure Key Vault: secret name `Secrets--{tenant}--{reference}`). **No vault package was added**: there is no vault to point it at.
- **Audit log:** `AttendanceAuditLog` (`SystemEntity`, insert-only: the DbContext refuses an update or delete), migration `AddAttendanceAuditLog`. `IAttendanceAuditTrail` stages entries in the caller's unit of work. Recorded: every correction decision (`CorrectionDecided`), every exception status change (`ExceptionStatusChanged`: raised, proposed, rejected, closed by a correction, auto-resolved) and every read of raw or normalized attendance outside normalization and processing (`AttendanceDataRead`: the health report, and the time-zone lookup of a proposal). `GET api/attendance/audit` lists it, newest first, filterable.

## Please look at

- **`TargetType` / `TargetId`, not `EntityType` / `EntityId`:** `Entity` already has an `EntityType` member (its CLR type), so the audit columns are named for their role. The API DTO still says `entityType` / `entityId`.
- **The Web has no sign-in.** It does not forward a token, so its calls get 401 anywhere but Development. **In Development** `Authentication:DevelopmentUser:Enabled` (on in `appsettings.Development.json`) signs every token-less request in as a stand-in user with every role (no `tenant_id`, so tenant selection stays by header; no `employee_id` unless configured, so proposing a correction as that user needs `Authentication:DevelopmentUser:EmployeeId`). OpenID Connect sign-in in the Web plus a handler that forwards the access token is the missing piece.
- **HR screens are locked to HRAdministrator too**, which the prompt did not ask for: leaving them open after adding authentication seemed wrong.
- **The overtime question of P11 is still open.** Roles now exist, but nothing approves a `PendingReview` record, and a correction still only supplies a missing punch.
- **Approver scope is still missing.** A manager sees every correction awaiting a manager; there is no team or department structure to scope by (`TODO(approver scope)`).
- **Reads of attendance by the health report write a row each call.** That is what the prompt asks for ("every direct read"); a dashboard that polls will fill the table. Consider sampling or dropping this one if it does.
- **Audit retention is undecided:** the table only grows.
- **`IsHrDecision` is still a request field**, but only for a user holding both roles.

## Tests

- `ApiAuthorizationTests` (19): real requests through the real controllers. An Employee can not call the decision endpoint or the approvers' queue; a PayrollAdministrator can read the payroll gate but not decide; nobody else can read it; no token is 401; a manager decides as themselves and the body can't make them HR; both-role users choose; a request body can't name another requester; every action names its roles.
- `ApiAuthenticationTests` (13): a real signed JWT through `AddRobeemAuthentication` (the provider's metadata replaced by a local key): claims keep their names, roles decide access, a wrong audience, issuer, key or an expired token is 401, the stand-in user answers only token-less requests and never replaces a bad token, and the startup guards.
- `AttendanceAuditTrailTests` (6) and the new cases of `AttendanceCorrectionServiceTests`: a correction decision writes an entry with the decider as actor; each exception status change is recorded with its actor; reads are audited; entries can't be changed or deleted; the trail is per tenant.
- `ConfigurationSecretStoreTests` (8) and two sync tests: a secret is per tenant, never found across tenants, never looked up without a tenant, handed to the adapter, and absent from the config's text.
