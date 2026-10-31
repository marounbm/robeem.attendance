# Deploying robeem Attendance on IIS

This guide publishes the two web applications, configures IIS, creates the PostgreSQL database and, last of all, starts the Api so that it migrates the database. The deployment is a proof of concept, called **robeem attendance PoC** below.

| Site | Project | Role |
|---|---|---|
| Api | `src/robeem.Api` | The only process that touches a database. Also runs the background jobs (normalization, integration sync, nightly reconciliation). |
| Web | `src/robeem.Web` | MVC front end. Reaches data only through the Api over HTTP. |

Order of work:

1. [Prepare the deliverable](#1-prepare-the-deliverable-build-machine)
2. [Prepare the server](#2-prepare-the-server)
3. [Create the IIS sites](#3-create-the-iis-sites)
4. [Create the database](#4-create-the-database-robeem-attendance-poc)
5. [Configuration](#5-configuration)
6. [Authentication: the PoC runs in Development mode](#6-authentication-the-poc-runs-in-development-mode)
7. [Start, migrate and verify](#7-start-migrate-and-verify)

For the architecture see [`README.md`](README.md) and [`CLAUDE.md`](CLAUDE.md); for local development see [`SETUP.md`](SETUP.md).

> **Read this first: the PoC has no sign-in and runs in Development mode.** The Web has no login and does not forward tokens (`CLAUDE.md` §2.8, "Not done: Web sign-in"), and outside Development the Api accepts only JWT bearer tokens. For this proof of concept both sites therefore run as on a developer machine: every visitor is an administrator with every role. Keep the sites on a closed network. See [section 6](#6-authentication-the-poc-runs-in-development-mode).

---

## 1. Prepare the deliverable (build machine)

Requirements: .NET SDK 10.

```powershell
cd D:\Workplace\robeem\robeem.attendance
dotnet build robeem.attendance.slnx
dotnet test  robeem.attendance.slnx

dotnet publish src/robeem.Api -c Release -o D:\@Deliveries\robeem.attendance\DATE\robeem-api
dotnet publish src/robeem.Web -c Release -o D:\@Deliveries\robeem.attendance\DATE\robeem-web
```

- Publishing generates a `web.config` in each folder for the ASP.NET Core Module. Do not hand-edit it: it is regenerated on every publish.
- Do **not** ship `appsettings.Development.json`. It lists `localhost` tenants and turns on the development user. Delete it from the publish folders.
- Zip the two folders and copy them to the server.

---

## 2. Prepare the server

1. Install the **IIS** role with **Management Tools**.
2. Install the **.NET 10 Hosting Bundle** (it includes the ASP.NET Core Module), then run `iisreset`. The .NET runtime or SDK alone is not enough: only the Hosting Bundle adds the IIS module. Install it after IIS, or run its installer again and choose *Repair*. Check it:

   ```powershell
   Import-Module WebAdministration
   Test-Path "$env:windir\system32\inetsrv\config\schema\aspnetcore_schema_v2.xml"   # must be True
   Get-WebGlobalModule | Where-Object Name -like 'AspNetCore*' | Select-Object Name   # must list AspNetCoreModuleV2
   ```

   Without the module, every request to the sites fails with `HTTP Error 500.19`, code `0x8007000d`.
3. Obtain a TLS certificate for `preview.robeem.com`, import it into the server's Local Machine certificate store and note its thumbprint (section 2.1). Both apps call `UseHttpsRedirection` and `UseHsts`.
4. Both sites use the one domain `preview.robeem.com` and are told apart by port. The Web calls the Api by URL, so the certificate must be valid for `preview.robeem.com`:
   - Web: `https://preview.robeem.com:8080`
   - Api: `https://preview.robeem.com:9080`
5. PostgreSQL is already installed on this server (section 4). Only the Api needs access to it.

### 2.1 Import the certificate and get its thumbprint

The bindings in section 3 need the certificate's thumbprint. The certificate must be in the **Local Machine** store (`Cert:\LocalMachine\My`), include its **private key**, be valid for `preview.robeem.com`, and not be expired.

1. If you received a `.pfx` file, import it (run as Administrator). Enter its password when asked:

   ```powershell
   $pfxPassword = Read-Host 'PFX password' -AsSecureString
   Import-PfxCertificate -FilePath 'C:\certs\preview.robeem.com.pfx' -CertStoreLocation Cert:\LocalMachine\My -Password $pfxPassword
   ```

   The command prints the imported certificate with its thumbprint. A `.cer` or `.crt` file has no private key and cannot be used for a binding.

2. List the certificates that match the domain, with the checks that matter:

   ```powershell
   Get-ChildItem Cert:\LocalMachine\My |
     Where-Object { $_.DnsNameList.Unicode -contains 'preview.robeem.com' -or $_.DnsNameList.Unicode -contains '*.robeem.com' } |
     Select-Object Thumbprint, Subject, NotAfter, HasPrivateKey, @{ n = 'DnsNames'; e = { $_.DnsNameList.Unicode -join ', ' } }
   ```

   Pick the row with `HasPrivateKey` = `True` and a `NotAfter` date in the future. A wildcard certificate (`*.robeem.com`) is valid for `preview.robeem.com`.

3. Fetch the thumbprint of the certificate to use. This stops with an error unless exactly one valid certificate matches (if several do, take the one you want from the list in step 2):

   ```powershell
   $domain = 'preview.robeem.com'
   $certs = @(Get-ChildItem Cert:\LocalMachine\My | Where-Object {
       $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
       ($_.DnsNameList.Unicode -contains $domain -or $_.DnsNameList.Unicode -contains '*.robeem.com') })
   if ($certs.Count -ne 1) { throw "Expected one certificate for $domain, found $($certs.Count)." }
   $certs[0].Thumbprint
   ```

4. **Write the thumbprint down.** Section 3, step 5 takes it as the value of `$thumb`. Copy it exactly, with no spaces.

Without PowerShell: run `certlm.msc`, open *Personal* → *Certificates*, double-click the certificate, and on the *Details* tab select *Thumbprint*. Copying it from there can add a hidden character at the start, so retype it or paste it into a plain-text editor first. In IIS Manager the same value is under *Server Certificates*.

For a closed test only, you can create a self-signed certificate (browsers will warn, and the Web server must trust it, section 5.3):

```powershell
$cert = New-SelfSignedCertificate -DnsName 'preview.robeem.com' -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(1)
$cert.Thumbprint
```

If the certificate is not issued by a public authority, the root certificate must also be trusted on every machine that opens the site, including the Web server itself, because it calls the Api.

---

## 3. Create the IIS sites

Run PowerShell as Administrator on the server. Variables such as `$root` exist only in the PowerShell window where they were set, so each code block below sets what it needs; run the steps in order. The values below are the PoC names; change the certificate and paths to yours. The robeem installation on the same server is not touched: use PoC-specific names for the sites and app pools, and ports that the existing robeem sites do not use.

### 3.1 Prepare the two websites, step by step

**Step 1. DNS and ports.** `preview.robeem.com` must resolve to the server (a DNS record, or a `hosts` entry for a closed test). Both sites use that name, on different HTTPS ports:

| Site | URL |
|---|---|
| Web (front end) | `https://preview.robeem.com:8080` |
| Api | `https://preview.robeem.com:9080` |

Port 8080 is the front end and 9080 the Api, as decided. If either is taken, change it everywhere it appears below. Check that neither port is already used on the server (for example by the existing robeem sites); this should print nothing:

```powershell
Get-NetTCPConnection -State Listen -LocalPort 8080, 9080 -ErrorAction SilentlyContinue
```

**Step 2. Folders.** Copy the unzipped publish output (section 1) into one folder per site, and create the shared `logs` folder next to them. Keep it all apart from the existing robeem sites:

```powershell
$root = 'C:\inetpub\robeem-attendance-poc'
New-Item -ItemType Directory -Force "$root\api", "$root\web", "$root\logs" | Out-Null
# copy the contents of robeem-api into $root\api, and robeem-web into $root\web
Remove-Item "$root\api\appsettings.Development.json", "$root\web\appsettings.Development.json" -ErrorAction SilentlyContinue
```

Check that `web.config` and `robeem.Api.dll` / `robeem.Web.dll` sit directly in each folder, not in a sub-folder.

**Step 3. Application pools.** One per site, with **No Managed Code**:

```powershell
Import-Module WebAdministration
foreach ($pool in 'robeem-attendance-poc-api', 'robeem-attendance-poc-web') {
    New-WebAppPool -Name $pool
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedPipelineMode -Value 'Integrated'
}
```

The Api pool needs the extra settings in step 6. The Web pool keeps the defaults.

**Step 4. Folder permissions.** Each pool identity reads its own site. Both may write to the shared `logs` folder:

```powershell
$root = 'C:\inetpub\robeem-attendance-poc'   # same value as in step 2; a new PowerShell window has no $root
icacls "$root\api"  /grant 'IIS AppPool\robeem-attendance-poc-api:(OI)(CI)RX'
icacls "$root\web"  /grant 'IIS AppPool\robeem-attendance-poc-web:(OI)(CI)RX'
icacls "$root\logs" /grant 'IIS AppPool\robeem-attendance-poc-api:(OI)(CI)M' 'IIS AppPool\robeem-attendance-poc-web:(OI)(CI)M'
```

**Step 5. Websites and bindings.** Each site has a single HTTPS binding, on its own port, with the certificate for `preview.robeem.com`. There is no HTTP binding: nothing listens on plain HTTP for these sites. The ASP.NET Core Module reads the one HTTPS port and passes it to the app, so `UseHttpsRedirection` knows where to send a request.

```powershell
Import-Module WebAdministration
$root = 'C:\inetpub\robeem-attendance-poc'   # same value as in step 2
$thumb = '<certificate thumbprint>'   # the value fetched in section 2.1, step 3
$domain = 'preview.robeem.com'

$sites = @(
    @{ Name = 'robeem-attendance-poc-api'; Path = "$root\api"; Port = 9080 },
    @{ Name = 'robeem-attendance-poc-web'; Path = "$root\web"; Port = 8080 }
)

# Stop here if the thumbprint is not a certificate in the store (a typo or a hidden character gives
# "Value does not fall within the expected range" from AddSslCertificate).
$thumb = $thumb -replace '[^0-9A-Fa-f]', ''
if (-not (Test-Path "Cert:\LocalMachine\My\$thumb")) { throw "No certificate with thumbprint '$thumb' in Cert:\LocalMachine\My." }

# Safe to run again: each part is skipped when it is already done.
foreach ($s in $sites) {
    if (-not (Get-Website -Name $s.Name)) {
        # the pool has the same name as the site; New-Website makes it with an http binding and starts it
        New-Website -Name $s.Name -PhysicalPath $s.Path -ApplicationPool $s.Name -Port $s.Port -HostHeader $domain | Out-Null
    }
    if (-not (Get-WebBinding -Name $s.Name -Protocol https)) {
        New-WebBinding -Name $s.Name -Protocol https -IPAddress '*' -Port $s.Port -HostHeader $domain
    }
    # Remove-WebBinding must be given the port and host header; with only -Protocol it fails with "Cannot find binding '*:*:*'"
    if (Get-WebBinding -Name $s.Name -Protocol http) {
        Remove-WebBinding -Name $s.Name -Protocol http -IPAddress '*' -Port $s.Port -HostHeader $domain
    }
    (Get-WebBinding -Name $s.Name -Protocol https).AddSslCertificate($thumb, 'My')
}

Get-WebBinding | Where-Object { $_.ItemXPath -like '*robeem-attendance-poc*' } | Select-Object protocol, bindingInformation, sslFlags
```

The last line lists the bindings. Each site should show only an `https` row, on its own port. If `AddSslCertificate` says the certificate is already bound (a re-run), that is fine.

`New-Website` starts the site. Stop both for now (they have no database or settings yet); they are started in section 7:

```powershell
Stop-Website robeem-attendance-poc-api
Stop-Website robeem-attendance-poc-web
```

**Step 6. Pool tuning.**

*Api pool.* The Api hosts the scheduler in-process, with no distributed lock (`CLAUDE.md` §2.8). These settings keep it running, and keep it to one instance:

| Setting | Value | Why |
|---|---|---|
| Start Mode | `AlwaysRunning` | Start at boot, not on the first request. |
| Site Preload Enabled | `true` | Start the app as soon as the pool starts. |
| Idle Time-out | `0` | Otherwise IIS stops the process after 20 minutes without requests, and the jobs stop with it. |
| Maximum Worker Processes | `1` | One instance only. |
| Disable Overlapped Recycle | `True` | Otherwise the old and new processes run together briefly and could normalize the same raw row twice. |
| Regular Time Interval | `0`, and a fixed recycle time in the night, away from the 02:00 reconciliation | Avoid the default 29-hour recycle at a random time. |

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
& $appcmd set apppool robeem-attendance-poc-api /startMode:AlwaysRunning `
  /processModel.idleTimeout:00:00:00 `
  /recycling.disallowOverlappedRotation:true `
  /recycling.periodicRestart.time:00:00:00
& $appcmd set app "robeem-attendance-poc-api/" /preloadEnabled:true
```

Adjust the site name in the last command to yours. Do not run the Api on several servers behind a load balancer until a distributed lock exists.

*Web pool.* The default settings are fine.

**Step 7. Firewall.** Open inbound TCP 8080 (Web) and 9080 (Api). The PoC has no sign-in (section 6), so also limit who can connect: add `-RemoteAddress <office network, e.g. 192.168.1.0/24>` to the rule below.

```powershell
New-NetFirewallRule -DisplayName 'robeem attendance PoC (HTTPS 8080, 9080)' `
  -Direction Inbound -Protocol TCP -LocalPort 8080, 9080 -Action Allow
```

Check it:

```powershell
Get-NetFirewallRule -DisplayName 'robeem attendance PoC*' | Get-NetFirewallPortFilter | Select-Object Protocol, LocalPort
```

The rule applies to all network profiles. To limit it, add `-Profile Domain` or `-RemoteAddress <address range>`. To remove it later: `Remove-NetFirewallRule -DisplayName 'robeem attendance PoC (HTTPS 8080, 9080)'`. Do not open the PostgreSQL port to other machines.

**Step 8. Log files.** The logs go to `C:\inetpub\robeem-attendance-poc\logs`. Startup errors are written by the ASP.NET Core Module, which only logs when told to. Turn that on in each site's `web.config` (the `aspNetCore` element) while you set up, and off again afterwards:

```xml
<aspNetCore ... stdoutLogEnabled="true" stdoutLogFile="..\logs\api-stdout" />   <!-- Api -->
<aspNetCore ... stdoutLogEnabled="true" stdoutLogFile="..\logs\web-stdout" />   <!-- Web -->
```

The path is relative to the site folder, and the module adds a timestamp and process id to the file name. `web.config` is regenerated by every publish, so set it again after an upgrade.

The application settings (environment variables) are set in section 5, after the database exists.

---

## 4. Create the database (robeem attendance PoC)

Each tenant is its own PostgreSQL database, with the schemas `HR` and `Attendance`. The PoC uses one tenant. The names below are used throughout this guide.

| Item | Value |
|---|---|
| Database | `robeem_attendance_poc` |
| Login role | `robeem_attendance_poc_user` |
| Tenant id (in the Api configuration) | `poc` (lower-case letters, digits and `-` only) |
| Tenant name (shown in the tenant picker) | `Robeem Attendance PoC` |

The robeem installation on the same server is not touched: the PoC has its own database and role, so it can share the PostgreSQL instance (same port) without sharing data or logins. Do not reuse robeem's role or database.

This section only **creates** the empty database and its owner. The tables and seed data are created by the migration at the end (section 7.1).

### 4.1 Create the role and the database

Open PowerShell on the server and connect as the PostgreSQL superuser. The `psql` path depends on your version:

```powershell
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h localhost -p 5432 -U postgres
```

Then run, inside `psql`:

```sql
CREATE ROLE robeem_attendance_poc_user LOGIN;
\password robeem_attendance_poc_user

CREATE DATABASE robeem_attendance_poc OWNER robeem_attendance_poc_user ENCODING 'UTF8';
```

- `\password` asks for the password twice, so it is not written in a script or in the shell history. Use a strong one without spaces or semicolons, because the connection string below is `;`-separated.
- The role is the **owner** of the database. That is enough: the Api, connected as this role, creates the `HR` and `Attendance` schemas, tables and indexes itself. The role needs no `SUPERUSER` and no `CREATEDB`, and should not have them.
- The database has to exist before the Api starts.

Check it (still in `psql`):

```sql
\l robeem_attendance_poc
\du robeem_attendance_poc_user
```

Leave with `\q`, then test the new login. Enter the password when asked:

```powershell
Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -W -h localhost -p 5432 -U robeem_attendance_poc_user -d robeem_attendance_poc -c "select current_user, current_database();"
```

`-W` forces the password prompt, and the first line makes sure a `PGPASSWORD` left in the window is not used instead.

In a classic command prompt (`cmd`, prompt like `D:\...>`) the PowerShell syntax above does not work (`& was unexpected at this time`). Use, from the folder that holds `psql.exe`:

```bat
set PGPASSWORD=
psql.exe -W -h localhost -p 5432 -U robeem_attendance_poc_user -d robeem_attendance_poc -c "select current_user, current_database();"
```

If it says `password authentication failed`, the role has no password or the one typed is wrong (`psql` shows nothing while you type). If `psql` never asked for a password, it sent a stored one: check the `PGPASSWORD` environment variable and `%APPDATA%\postgresql\pgpass.conf`, and add `-W` to force the prompt. Connect as `postgres` again and run `\password robeem_attendance_poc_user`, then repeat the test. `psql.exe` does not have to come from `C:\Program Files\PostgreSQL\17\bin`; for example pgAdmin's `runtime` folder has one too (`cd /d "<folder>"` first, or call it by its full path).

If the login is refused with `no pg_hba.conf entry`, check `pg_hba.conf` (in the PostgreSQL `data` folder). It needs lines that allow the connection from `127.0.0.1/32` and `::1/128` with `scram-sha-256`, which is the default on current versions. After a change, reload the configuration or restart the PostgreSQL Windows service.

### 4.2 The connection string

The Api and PostgreSQL are on the same server, so:

```
Host=localhost;Port=5432;Database=robeem_attendance_poc;Username=robeem_attendance_poc_user;Password=<password>
```

It goes into the Api configuration as `Tenancy__Tenants__0__ConnectionString` (section 5.2). It is not stored in this file or in the repository.

### 4.3 Backup and removal

- Backup: `pg_dump -h localhost -U robeem_attendance_poc_user -Fc -f robeem_attendance_poc.dump robeem_attendance_poc`
- To start again from scratch (this destroys the PoC data): stop the Api site, then as `postgres` run `DROP DATABASE robeem_attendance_poc;` and create it again (section 4.1).
- To remove the PoC completely: `DROP DATABASE robeem_attendance_poc;` then `DROP ROLE robeem_attendance_poc_user;`.

### 4.4 Adding a tenant later

Create another database and owner role as in 4.1, add the next index under `Tenancy:Tenants` (`__1__`), and restart the Api with `InitializeOnStartup=true` (section 7.1).

---

## 5. Configuration

### 5.1 Where to put settings

Environment variables use `__` for the section separator and a number for an array index. Setting them on the **app pool** keeps passwords out of the deliverable:

For one variable the command is:

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
& $appcmd set config -section:system.applicationHost/applicationPools `
  /+"[name='robeem-attendance-poc-api'].environmentVariables.[name='Tenancy__Tenants__0__Id',value='poc']" /commit:apphost
```

To set many, define this helper once. Run it in the PowerShell window that you also use for 5.2 and 5.3. It removes an old value first, so it can be run again to change a value:

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
function Set-PoolEnv([string]$Pool, [string]$Name, [string]$Value) {
    $section = '-section:system.applicationHost/applicationPools'
    & $appcmd set config $section "/-[name='$Pool'].environmentVariables.[name='$Name']" /commit:apphost 2>$null | Out-Null   # fails quietly when not set yet
    & $appcmd set config $section "/+[name='$Pool'].environmentVariables.[name='$Name',value='$Value']" /commit:apphost | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not set $Name on $Pool." }
}
```

Values cannot contain a single quote, a comma, a square bracket or a double quote, because of the `appcmd` syntax. Changing an app pool's environment variables recycles the pool. Alternative: an `appsettings.Production.json` next to the Api, with NTFS permissions limited to Administrators and the app pool identity.

Never commit passwords. The values are stored in plain text in `applicationHost.config` (administrators only) or in the JSON file.

### 5.2 Api settings

| Setting (environment variable) | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` (the PoC decision, section 6) |
| `Authentication__DevelopmentUser__Enabled` | `true` (stand-in user with every role; the Api refuses this outside Development) |
| `Tenancy__AllowUnauthenticatedTenantSelection` | `true` (same refusal rule) |
| `Tenancy__DefaultTenantId` | `poc` (the tenant used when none is chosen) |
| `Database__InitializeOnStartup` | `true`. In Development this normally comes from `appsettings.Development.json`, which is not deployed, so it is set here (section 7.1) |
| `Tenancy__Tenants__0__Id` | `poc` |
| `Tenancy__Tenants__0__Name` | `Robeem Attendance PoC` |
| `Tenancy__Tenants__0__ConnectionString` | `Host=localhost;Port=5432;Database=robeem_attendance_poc;Username=robeem_attendance_poc_user;Password=<pwd>` (section 4.2) |
| `Secrets__poc__<reference>` | The secret that an `AttendanceConnection.SecretRef` names (webhook key, API password, decryption key) |

Notes:

- A bad tenant entry fails the Api at startup, on purpose.
- Secrets are resolved from `Secrets:{tenantId}:{reference}`. A production setup can instead use any configuration provider, such as Azure Key Vault.
- Optional tuning is in `appsettings.json` under `Attendance:Normalization` and `Attendance:Sync` (poll intervals, retries, circuit breaker, reconciliation cron `0 2 * * *`).

Set them on the Api pool (the helper from 5.1 must be defined in this window). No identity provider values are needed (section 6). The password is typed at a prompt, so it is not written in the command or in the shell history:

```powershell
if (-not (Get-Command Set-PoolEnv -ErrorAction SilentlyContinue)) { throw 'Define Set-PoolEnv first (section 5.1).' }
$api = 'robeem-attendance-poc-api'

$secure = Read-Host 'Password of robeem_attendance_poc_user' -AsSecureString
$dbPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

$settings = [ordered]@{
    'ASPNETCORE_ENVIRONMENT'                       = 'Development'
    'Authentication__DevelopmentUser__Enabled'     = 'true'
    'Tenancy__AllowUnauthenticatedTenantSelection' = 'true'
    'Tenancy__DefaultTenantId'                     = 'poc'
    'Database__InitializeOnStartup'                = 'true'
    'Tenancy__Tenants__0__Id'                      = 'poc'
    'Tenancy__Tenants__0__Name'                    = 'Robeem Attendance PoC'
    'Tenancy__Tenants__0__ConnectionString'        = "Host=localhost;Port=5432;Database=robeem_attendance_poc;Username=robeem_attendance_poc_user;Password=$dbPassword"
}

foreach ($name in $settings.Keys) { Set-PoolEnv $api $name $settings[$name] }

# a secret that a connection names (section 5.2 table), only when you have one:
# Set-PoolEnv $api 'Secrets__poc__<reference>' '<secret>'

Remove-Variable dbPassword, secure, settings
```

Check the names and values, with the password hidden:

```powershell
Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' `
  -Filter "system.applicationHost/applicationPools/add[@name='$api']/environmentVariables/add" |
  Select-Object name, @{ n = 'value'; e = { $_.value -replace 'Password=[^;]*', 'Password=***' } }
```

There should be one row per variable (nine for the Api pool). Variables from an earlier run that this script no longer sets (`Authentication__Jwt__*`) may still be listed; section 6 shows how to remove them.

### 5.3 Web settings

| Setting | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` (the PoC decision, section 6) |
| `RobeemApi__BaseUrl` | The Api's absolute URL, `https://preview.robeem.com:9080/` (validated at startup) |

```powershell
if (-not (Get-Command Set-PoolEnv -ErrorAction SilentlyContinue)) { throw 'Define Set-PoolEnv first (section 5.1).' }
$web = 'robeem-attendance-poc-web'
Set-PoolEnv $web 'ASPNETCORE_ENVIRONMENT' 'Development'
Set-PoolEnv $web 'RobeemApi__BaseUrl' 'https://preview.robeem.com:9080/'

Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' `
  -Filter "system.applicationHost/applicationPools/add[@name='$web']/environmentVariables/add" |
  Select-Object name, value
```

The Web has no tenant or database configuration. The tenant list comes from `GET api/tenants`. The Web server must be able to resolve and reach the Api's name and trust its certificate, so a self-signed certificate has to be installed there as trusted.

---

## 6. Authentication: the PoC runs in Development mode

**Decision: this is a proof of concept, so both sites run exactly as they do on a developer machine** (`CLAUDE.md` §2.8, Development). There is no sign-in and no identity provider. Nothing in this section needs to be done on the server; it explains what the settings in section 5 do.

What Development mode means for the PoC:

| Setting | Value | Effect |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` on **both** pools | The Api accepts the two settings below (it refuses them in any other environment). The Web shows its *Act as* screen. Both sites show detailed error pages and do not send the HSTS header. |
| `Authentication__DevelopmentUser__Enabled` | `true` (Api) | Every request without a bearer token is authenticated as a stand-in user with **every role** (`Employee`, `Manager`, `HRAdministrator`, `PayrollAdministrator`, `IntegrationAdministrator`). |
| `Tenancy__AllowUnauthenticatedTenantSelection` | `true` (Api) | The tenant is taken from the `X-Tenant-Id` header, which the Web sends from the tenant picker's cookie, else from `Tenancy__DefaultTenantId`. |
| `Tenancy__DefaultTenantId` | `poc` (Api) | The one PoC tenant is used without picking it. |

No `Authentication__Jwt__Authority` or `Audience` is needed: they are required only outside Development. The Web calls the Api with no token, and the Api answers as the stand-in user, so the Web works.

**Consequences, read before opening the sites to anyone:**

- **The Api has no security of its own.** Anyone who can reach `https://preview.robeem.com:9080` can read and change all PoC data, as any role, in any configured tenant. Browsers never call the Api (the Web does, server to server), so users need only port 8080. Limit the firewall rule of section 3.1, step 7, to your office network or to named addresses with `-RemoteAddress`, and do not publish port 9080 to the internet.
- **The Web has no login either.** Whoever can open `https://preview.robeem.com:8080` is an administrator. Use real data only if that is acceptable, and keep the PoC off the public internet (a VPN, an address allow-list in the firewall, or IIS *IP Address and Domain Restrictions*).
- **Do not leave `appsettings.Development.json` in the publish folders.** Development mode loads it, and it configures the developer tenants `demo` and `acme` (databases `robeem_demo` and `robeem_acme`, no passwords), turns on `Database:InitializeOnStartup` and sample data, and makes `demo` the default tenant. Section 3.1, step 2 deletes it; check that it is gone. Everything the PoC needs is set in section 5.
- **Before real use,** switch to `Production` and a real identity provider: set `ASPNETCORE_ENVIRONMENT=Production`, set `Authentication__DevelopmentUser__Enabled` and `Tenancy__AllowUnauthenticatedTenantSelection` to `false`, and set `Authentication__Jwt__Authority` and `Audience`. Because the Web still has no sign-in, that also needs the OpenID Connect sign-in described in `CLAUDE.md` §2.8. In Production the identity provider's token must carry the claims `tenant_id` (here `poc`), `role` (one per role) and `employee_id`.

**Optional: sample employees.** To fill the PoC with sample employees, add `Database__SampleData__TenantIds__0` = `poc` to the Api settings. It only matters on the first migration (section 7.1). Leave it out for an empty database.

**If you already set the Production values** (the earlier 5.2 version), the next run of the 5.2 script replaces them. `Authentication__Jwt__Authority` and `Authentication__Jwt__Audience` can stay, as they are ignored in Development, but you can remove them:

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
foreach ($name in 'Authentication__Jwt__Authority', 'Authentication__Jwt__Audience') {
    & $appcmd set config '-section:system.applicationHost/applicationPools' "/-[name='robeem-attendance-poc-api'].environmentVariables.[name='$name']" /commit:apphost
}
```

---

## 7. Start, migrate and verify

The tables and seed data are created last, by the Api itself, on its first start.

### 7.1 Migrate: create the tables and seed data

The schema is created by the Api, not by a script. `Database:InitializeOnStartup` is `false` outside Development, so enable it for the first start:

- Set `Database__InitializeOnStartup=true` on the Api's app pool (section 5.2).
- At startup the Api runs `TenantDatabaseInitializer` for each configured tenant: `MigrateAsync`, then the idempotent seed contributors (vendor catalog, payload formats, default attendance policy, the *Standard Week* schedule).
- After the first successful start you can set it back to `false`. Turn it on again for every upgrade (section 8).

To review the SQL first, generate it on the build machine and run it as `robeem_attendance_poc_user` against the PoC database:

```powershell
dotnet ef migrations script --idempotent `
  --project src/robeem.Infrastructure --startup-project src/robeem.Api `
  -o robeem-schema.sql
```

The script creates the schema only. Seed data is still applied by the Api at startup, so you need `InitializeOnStartup=true` once either way.

### 7.2 Start and verify

1. Start the Api site first. This runs the migration (7.1):

   ```powershell
   Start-Website robeem-attendance-poc-api
   ```

2. If it does not start, check the Windows Event Log (source *IIS AspNetCore Module*). For a log file, turn on `stdoutLogEnabled` as in step 8 of section 3.1; the files are in `C:\inetpub\robeem-attendance-poc\logs`. Turn it off again afterwards. Common causes:
   - `...DevelopmentUser:Enabled authenticates everyone ... only allowed in Development`: `ASPNETCORE_ENVIRONMENT` is not `Development` on the Api pool
   - `Authentication:Jwt:Authority and :Audience must be set`: the same cause, the Api is running as Production
   - a malformed tenant entry or connection string
   - the database does not exist, the password is wrong, or the role does not own the database (it must be able to create schemas and tables)
3. Open `https://preview.robeem.com:9080/api/tenants`. It is anonymous and should list `Robeem Attendance PoC`.
4. Check the database (7.3).
5. Start the Web site and open `https://preview.robeem.com:8080`. There is no sign-in (section 6): you are the stand-in administrator, and the *Act as* screen (Development only) lets you pick an employee to act as.

   ```powershell
   Start-Website robeem-attendance-poc-web
   ```

6. Set `Database__InitializeOnStartup` back to `false` if you want to.
7. After a few minutes, confirm the background jobs are running: the Web's *Background jobs* screen (HR and integration administrators) lists the runs.

### 7.3 Check the migrated database

After the Api has started once:

```sql
\c robeem_attendance_poc
\dn
SELECT count(*) FROM "Attendance"."AttendanceVendor";
SELECT count(*) FROM "HR"."TimeSchedule";
```

`\dn` should list `HR` and `Attendance`, and both counts should be above zero.

---

## 8. Upgrades

1. Back up the PoC database (section 4.3).
2. Put an `app_offline.htm` file in each site folder, or stop the sites.
3. Publish the new build (section 1) and copy it over the old folders. Keep any `appsettings.Production.json` you use, and do not copy `appsettings.Development.json`.
4. Set `Database__InitializeOnStartup=true` and start the Api so new migrations are applied (section 7.1). Check the log or event viewer for errors.
5. Remove `app_offline.htm` from the Web, and verify as in section 7.2.

Rollback: restore the previous publish folders and, if a migration was applied, restore the database backup. Migrations are not reversed automatically.

---

## 9. Security checklist

- [ ] PoC (Development mode, section 6): the sites are reachable only from a closed network (firewall `-RemoteAddress`, VPN or IIS IP restrictions), and port 9080 is not open to users or the internet.
- [ ] PoC: `appsettings.Development.json` is not in the publish folders (it would add the `demo` and `acme` tenants).
- [ ] Before real use: `ASPNETCORE_ENVIRONMENT=Production`, `Authentication:DevelopmentUser:Enabled` and `Tenancy:AllowUnauthenticatedTenantSelection` are `false`, and an identity provider is configured (section 6).
- [ ] No passwords in files under version control or in the zip; the connection string and `Secrets__*` live in app pool environment variables or a secret store.
- [ ] HTTPS bindings with a valid certificate; no HTTP binding.
- [ ] `robeem_attendance_poc_user` owns only the PoC database and is not a superuser; PostgreSQL is not reachable from other machines unless needed.
- [ ] `appsettings.Development.json` is not in the publish folders.
- [ ] Backups are scheduled and a restore has been tried.
- [ ] Exactly one Api worker process (step 6 of section 3.1).
