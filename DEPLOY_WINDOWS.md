# Windows VPS: one domain for React and API

The IIS site runs the ASP.NET Core API and serves the compiled React app from
`wwwroot`. Browser requests to `/api/*` use the same domain. No second IIS
site, API port, CORS origin, or `VITE_API_URL` is needed in production.

The project publishes with IIS `hostingModel="outofprocess"`. Keep this value
in the generated `web.config`: on the current Windows VPS, in-process hosting
failed with HTTP 500.30 (`CLR worker thread exited prematurely`), while the
same API started successfully as a separate process and passed
`/health/ready`. The underlying in-process failure was not established.

This guide covers packaging and IIS setup. The application still stores JWTs
in browser `sessionStorage`, and a live security review has not been completed.
Do not open the CRM to real users until the production controls listed in the
[README](README.md) have been reviewed.

## 1. Prepare the VPS

- Install Windows Server Web Server (IIS) and the **.NET 10 Hosting Bundle**.
  Install IIS first, then the Hosting Bundle. Restart IIS after installation.
  On Windows Server, an elevated PowerShell can install IIS with
  `Install-WindowsFeature Web-Server -IncludeManagementTools`. Install the
  Hosting Bundle from Microsoft's [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0).
- Install and configure SQL Server on the VPS. If using the SQL login shown
  below, enable SQL Server authentication. Keep SQL Server off the public
  Internet. Use a login with database creation/schema permissions for the
  first migration; create a separate app login with only the permissions it
  needs after the database exists.
- Point the domain's DNS A record to the VPS public IP. In IIS, bind the domain
  to the site and add a valid TLS certificate with an HTTPS (443) binding.
- If the domain is still pending, use a temporary IIS HTTP binding on an
  available port (for example 8080) and test readiness by IP. Do not enter
  production login credentials over HTTP. Remove this temporary binding before
  normal use; use the domain's HTTPS binding for login.
- Create an IIS site with a dedicated application pool set to **No Managed
  Code**. Give `IIS AppPool\YOUR_POOL_NAME` read/execute access to the site
  folder and its files. The IIS physical path is the folder containing the
  extracted `web.config`, not its `wwwroot` child. Keep the site/app pool
  stopped until the production config and database migration are complete.

## 2. Build on a Windows development/build machine

Install .NET 10 SDK, Node.js 22+, and `dotnet-ef` 10.0.0. From the repository
root, stop any running Vite development server (it can lock `esbuild.exe` on
Windows), then run:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.0
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-windows.ps1
```

The script creates `artifacts\iis`, `artifacts\LeadManagement-IIS.zip`, and
`artifacts\efbundle.exe`. It checks that the development settings file is
absent from the IIS package. Transfer the **ZIP only** to the VPS and extract
its **contents** into the IIS site folder. `web.config`,
`appsettings.Production.json`, `efbundle.exe`, and `seed_kuwait_demo.sql` must be directly inside that
folder. Do not copy the repository's development settings file or
`frontend/.env.example`.

Before uploading, open the ZIP's `web.config` and confirm that its
`aspNetCore` element has `hostingModel="outofprocess"`. Confirm that
`wwwroot/index.html` is also in the ZIP. Do not copy an older `web.config`
from a prior release over the new one.

For a first deployment, extract with an elevated PowerShell on the VPS (adjust
the paths to your folders):

```powershell
$sitePath = 'C:\Sites\LeadManagement'
New-Item -ItemType Directory -Path $sitePath -Force | Out-Null
Expand-Archive -LiteralPath 'C:\Transfer\LeadManagement-IIS.zip' -DestinationPath $sitePath
```

Set the IIS site's **Physical Path** to `$sitePath`. On updates, preserve the
VPS production config before replacing files; do not blindly extract over it.

## 3. Configure production on the VPS

Edit `appsettings.Production.json` in the IIS site folder:

The IIS package runs in ASP.NET Core's default `Production` environment. No
Visual Studio environment variables are needed.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SQL_SERVER;Database=LeadManagement;User ID=crm_app;Password=YOUR_REAL_SQL_PASSWORD;Encrypt=True;TrustServerCertificate=False"
  },
  "Jwt": {
    "Key": "REPLACE_WITH_A_RANDOM_SECRET_OF_AT_LEAST_32_BYTES"
  },
  "AllowedOrigins": [],
  "CRM_ADMIN_EMAIL": "admin@YOUR_DOMAIN",
  "CRM_ADMIN_PASSWORD": "REPLACE_WITH_A_STRONG_UNIQUE_PASSWORD"
}
```

Use SQL Server's actual host and instance name. For a named instance, use
`Server=HOSTNAME\INSTANCE_NAME`. With `TrustServerCertificate=False`, SQL
Server needs a certificate trusted by the VPS whose name matches the host in
the connection string. Configure that certificate before starting the app.

Generate a new random JWT key of at least 32 bytes for production. Do not
reuse the development key or leave the sample value. Keep the production
config only on the VPS; the checked-in file and every new ZIP contain
placeholders. On later deployments, preserve the VPS copy before extracting
the ZIP and restore it afterward.

To generate a JWT key in PowerShell on the VPS:

```powershell
$keyBytes = [byte[]]::new(48)
$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $random.GetBytes($keyBytes) } finally { $random.Dispose() }
[Convert]::ToBase64String($keyBytes)
```

For initial admin creation, temporarily add `CRM_ADMIN_EMAIL` and
`CRM_ADMIN_PASSWORD` (12+ characters with upper and lower case, a number, and a
symbol) to this production file, as shown above. Remove those two entries after the first
successful start; the admin remains in the database. Keep this file readable
only by the site identity and server administrators. Back it up separately
before replacing the published files during future releases.

## 4. Create/update the database

Back up any existing database first. The generated InitialCreate migration is
for a **new empty** database. Existing installations that used the earlier SQL
upgrade script need their migration history reconciled before this bundle is
used.

From PowerShell in the IIS site folder, run the migration bundle with an
identity that can create or update the schema. The bundle reads the production
config from this folder; `--connection` selects the migration identity. If the
Windows account running PowerShell has SQL Server schema permissions:

```powershell
.\efbundle.exe --connection "Server=YOUR_SQL_SERVER;Database=LeadManagement;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=False"
```

Otherwise, use a separate elevated SQL login for the migration:

```powershell
.\efbundle.exe --connection "Server=YOUR_SQL_SERVER;Database=LeadManagement;User ID=YOUR_MIGRATION_USER;Password=YOUR_MIGRATION_PASSWORD;Encrypt=True;TrustServerCertificate=False"
```

After the migration, create/map the app SQL login in SQL Server Management
Studio and grant it `db_datareader` and `db_datawriter` on `LeadManagement`.
Use a different login for later schema migrations. Put the app login's
connection string in `appsettings.Production.json`. Delete
`efbundle.exe` from the site folder, then start the IIS site/app pool. Check
`https://YOUR_DOMAIN/health/ready` and
`https://YOUR_DOMAIN/`. The first endpoint should return `{"status":"ready"}`;
the second should show the login page. Sign in with the production admin
credentials, then remove the temporary admin credentials from the config.

When the domain is pending, check `http://VPS_IP:8080/health/ready` only. After
the domain and certificate are ready, remove the temporary HTTP binding and
perform the login check over HTTPS.

### Optional Kuwait demo data

The ZIP also contains `seed_kuwait_demo.sql`. After the **first successful API
start** has created the admin account, run this file in SQL Server Management
Studio against the new `LeadManagement` database. Alternatively, if `sqlcmd`
is installed and the Windows account has database write permission:

```powershell
sqlcmd -S 'YOUR_SQL_SERVER' -d LeadManagement -E -b -i .\seed_kuwait_demo.sql
```

It adds 5 demo staff, 24 synthetic customers and 37 follow-ups. The demo staff
have no password and cannot sign in until an admin sets one. The script only
runs against a database named `LeadManagement`, refuses to mix with real leads,
and skips a repeated run. Refresh the dashboard after seeding and delete the
SQL file from the IIS folder. The contacts are fictional and must not be used
for outreach.

If IIS returns **500.30**, check the Windows Event Viewer Application log for
the API startup error; the usual setup causes are an unchanged placeholder,
invalid JSON, SQL certificate/authentication failure, or a database that has
not been migrated. If `/` returns **404**, verify that `web.config` and
`wwwroot\index.html` are under the IIS physical path. If `/health/ready`
returns **503**, check the SQL connection and database permissions.

If the app starts with `dotnet .\LeadManagement.Api.dll --urls
http://127.0.0.1:5099` and a local request to `/health/ready` succeeds, but
IIS still shows 500.30, check that the deployed `web.config` uses
`hostingModel="outofprocess"`. Use a free port for the manual test and stop
that process afterward. Do not stop another app just because port 5000 is in
use. The generated handler has `verb="*"`, so IIS forwards all HTTP verbs;
each API verb still needs a matching endpoint in the application.

## Updates

Back up the SQL database and the VPS production settings before each update.
Build a new package, stop the IIS site, replace the published application
files while preserving the VPS `appsettings.Production.json`, apply new
migrations with the matching bundle, then start the site and check readiness.

Microsoft's [IIS hosting guide](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
and [EF Core migration deployment guide](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying)
cover the server prerequisites and migration options in more detail.
