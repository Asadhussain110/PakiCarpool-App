# PakiCarpool Flutter app

## Run on a real Android phone over LAN HTTP

The `local` flavor in both apps permits cleartext HTTP, including in a release
APK. The `production` flavor remains HTTPS-only. Local APKs show a **Server
address** field on the login screen; a saved value overrides the build-time
default. API and SignalR use the same address. Google sign-in is intentionally
hidden in local builds; use email/password. The backend must run in
`Development`, which keeps HTTPS redirection, HSTS, and Secure refresh cookies
off without changing non-Development behavior.

### 1. Configure and start the backend

From the repository root in PowerShell, create or update the root `.env` file
with the existing development values for these keys:

```text
DB_CONNECTION_STRING
JWT_SECRET
JWT_ISSUER
JWT_AUDIENCE
AES_KEY_V1 (or AES_ENCRYPTION_KEY)
SMTP_HOST
SMTP_PORT
SMTP_USER
SMTP_PASS
SMTP_FROM
SUPABASE_URL
SUPABASE_KEY
ADMIN_SEED_TOKEN
```

`DB_CONNECTION_STRING` is the single Postgres connection string used by both the
backend and EF Core migrations. `JWT_SECRET` must be at least 32 UTF-8 bytes; the
AES key must be Base64-encoded 32 bytes. In Development,
`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, and `APP_DOMAIN` are not required to
start because local builds hide Google sign-in. `REDIS_CONNECTION_STRING` is not
required in Development. No hCaptcha secret is required either: the app uses
hCaptcha's official test site key and the backend uses the official test secret.
Do not use those test keys outside Development. Email/SMTP is still needed for
email verification and password-reset codes.

The local Supabase connection string may use
`SSL Mode=Require;Trust Server Certificate=true` for development only. For
production, use `SSL Mode=VerifyFull;Trust Server Certificate=false` (or omit
`Trust Server Certificate`) so Npgsql verifies the server certificate and
hostname.

Apply the database migration and run with the existing `PakiCarpool.API`
launch profile (HTTP `0.0.0.0:58240`):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
# Use the same DB_CONNECTION_STRING you keep in the repo-root .env file.
# Example (masked): 'Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=***;SSL Mode=Require;Trust Server Certificate=true'
$env:DB_CONNECTION_STRING = '<copy the same value from your .env>'
dotnet ef database update --project .\PakiCarpool.Repository\PakiCarpool.Repository.csproj --startup-project .\PakiCarpool.API\PakiCarpool.API.csproj
dotnet run --project .\PakiCarpool.API\PakiCarpool.API.csproj --launch-profile PakiCarpool.API
```

Keep this terminal running. The backend uses `http://127.0.0.1:2322` for Photon
by default; see **Start Photon** below if place search is needed.

### 2. Allow inbound API traffic through Windows Firewall

Run this once in an elevated PowerShell window. It adds a private-network
inbound rule for the backend's TCP port:

```powershell
New-NetFirewallRule -DisplayName 'PakiCarpool local API TCP 58240' -Direction Inbound -Protocol TCP -LocalPort 58240 -Action Allow -Profile Private
```

The repository does not create or manage Windows Firewall rules.

### 3. Find the PC's Wi-Fi IPv4 address

Run this on the PC and use the IPv4 address for its Wi-Fi adapter (not the
router/gateway address):

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.InterfaceAlias -notmatch 'vEthernet|Loopback' } | Format-Table InterfaceAlias,IPAddress
```

For example, if the Wi-Fi address is `192.168.1.42`, the API base is
`http://192.168.1.42:58240/api/v1`.

### 4. Build both local release APKs

Run each command from its app directory, replacing the sample IP with the PC's
Wi-Fi IPv4 address:

```powershell
Set-Location '.\Frontend mockup\passenger_driver_app'
$api = 'http://192.168.1.42:58240/api/v1'
flutter build apk --release --flavor local --dart-define=LOCAL_BUILD=true "--dart-define=API_BASE_URL=$api"
```

Passenger APK:
`build\app\outputs\flutter-apk\app-local-release.apk`

```powershell
Set-Location '..\admin_app'
$api = 'http://192.168.1.42:58240/api/v1'
flutter build apk --release --flavor local --dart-define=LOCAL_BUILD=true "--dart-define=API_BASE_URL=$api"
```

Admin APK:
`build\app\outputs\flutter-apk\app-local-release.apk`

### 5. Install and set the server address

Enable USB debugging and connect the Android phone, or copy each APK to the
phone. If Android platform-tools are installed and `adb` is on PATH:

```powershell
Set-Location ..\..
adb install -r '.\Frontend mockup\passenger_driver_app\build\app\outputs\flutter-apk\app-local-release.apk'
adb install -r '.\Frontend mockup\admin_app\build\app\outputs\flutter-apk\app-local-release.apk'
```

If Android reports a signature mismatch because the production app is already
installed with a different signing key, uninstall that app first and then
install the local APK. Uninstalling removes that app's local data:

```powershell
adb uninstall pk.pakicarpool.pakicarpool_user_app
adb uninstall pk.pakicarpool.pakicarpool_admin_app
```

Open the local app and enter `http://192.168.1.42:58240/api/v1` in **Server
address** on the login screen, then save it. Use email/password for passenger
login. Keep the phone and PC on the same Wi-Fi; verify the address in the
phone's browser at `http://192.168.1.42:58240/health/live` if the app cannot
connect.

### 6. If the router isolates Wi-Fi clients

Enable the phone's hotspot, connect the PC to that hotspot, then find the PC's
new IPv4 address with the command in step 3. Re-run the firewall command with
`-Profile Any` if Windows classifies the hotspot network as Public:

```powershell
Set-NetFirewallRule -DisplayName 'PakiCarpool local API TCP 58240' -Profile Any
```

Rebuild with that new address or enter it in the app's **Server address**
field. Keep the phone hotspot enabled while using the app.

### Start Photon for place autocomplete (optional)

Photon must run on the PC because the backend calls its default address,
`http://127.0.0.1:2322`. Install Java 21 or later, download the Photon release
JAR and a matching Photon data extract (a Pakistan extract is sufficient), and
extract the data so the JAR's working directory contains `photon_data`. From
that directory, start Photon:

```powershell
$photonJar = Get-ChildItem .\photon-*.jar | Select-Object -First 1
if ($null -eq $photonJar) { throw 'Photon release JAR not found in this directory.' }
java -jar $photonJar.FullName serve
```

Photon's upstream download and setup instructions are at
[github.com/komoot/photon](https://github.com/komoot/photon). Leave it running
while testing. If unavailable, autocomplete shows an explicit error and pickup
can still be selected using the current-location option.

For an Android emulator, the API host is `10.0.2.2` instead of the PC's LAN IP.
Use `--dart-define=API_BASE_URL=http://10.0.2.2:58240/api/v1` when running a
local debug build.

## Place-search request chain

Both app calls use the versioned API base URL, and both endpoints require an
authenticated passenger or driver:

| App request | Backend route | Photon request |
| --- | --- | --- |
| `GET places/autocomplete?q=...&lang=...` | `GET /api/v1/places/autocomplete` | `GET /api/?q=...&limit=...&lang=...` |
| `GET places/reverse?lat=...&lon=...` | `GET /api/v1/places/reverse` | `GET /reverse?lat=...&lon=...` |

Autocomplete omits `limit` in the app request; the backend supplies Photon
`DefaultLimit` (5 by default). The backend returns its normal `{ data: ... }`
response envelope, which the mobile API client unwraps.

The backend's default Photon URL is `http://127.0.0.1:2322`, appropriate when
Photon runs on the same host as the backend. Override it through normal .NET
configuration only when Photon is hosted elsewhere. Set it in the backend
terminal before the run command in step 1:

```powershell
$env:Photon__BaseUrl = 'http://127.0.0.1:2322'
```

Use the address reachable from the backend process if Photon is hosted
elsewhere.
`Photon__DefaultLimit` can similarly override the autocomplete limit.

## Driver liveness

Driver liveness uses the live front-camera stream; it does not accept gallery
images. The app obtains a nonce from `GET driver/liveness/challenge`, selects
three random face prompts, and checks them locally with ML Kit face detection.
After the prompts pass, it captures a selfie and submits
`{ nonce, selfieBase64, promptResults }` to `POST driver/liveness`. Failed or
incomplete prompt results can be submitted for the existing admin review queue.
The pending screen polls `GET driver/liveness/status` and saves the refreshed
access token when an administrator approves the submission.

The server currently treats three supported, client-reported passing prompts as
an approval signal. Those booleans are not tamper-resistant proof: a modified
client can forge them. Production-grade automated approval requires a
server-verifiable liveness signal or device attestation; otherwise retain
administrator approval as the authority.

## Driver car documents

Upload each car photo or registration image as multipart form data (`file` and
`documentType`, either `carPhoto` or `registration`) to
`POST driver/car/documents` before registering or updating a car. The endpoint
accepts JPEG/PNG images up to 5 MB and returns `{ data: { path } }`. Car-photo
paths are public URLs; registration paths are opaque keys in the private
`driver-registration-documents` bucket. Only admins can obtain a five-minute
signed registration URL through
`GET /api/v1/admin/driver-approvals/{carId}/registration-document`. Send the
returned paths in `photoPaths` and `regDocPath`; car endpoints do not accept
base64 image data in these fields. Before deployment, create the Supabase
`driver-registration-documents` bucket with public access disabled.
