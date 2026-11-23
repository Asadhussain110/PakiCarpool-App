# Paki Carpool — Guardrails

## Recommendation (Address Autocomplete Index)
Use the **pre-built Asia search index** for self-hosted Photon, run as a systemd service directly on the host (Java, no Docker/WSL2). Rationale:
- Pre-built Asia index is maintained upstream — no manual extract/import pipeline to keep in sync.
- Faster to stand up and re-deploy; no build-time cost on every server refresh.
- Coverage is sufficient for Pakistani cities/routes; a custom-built index adds ongoing maintenance for marginal precision gain.
- Keeps infra simple, consistent with the "no third-party paid APIs" constraint — no extra tooling dependency for index generation.
- Running Photon as a plain systemd service on the host (rather than in Docker/WSL2) removes a virtualization layer with no functional benefit on this deployment target.

All autocomplete requests still proxy through the ASP.NET Core API; Photon binds to `127.0.0.1` only and is never exposed directly.

---

## Architecture (NEVER BREAK)
- 7 projects: Domain → Common → Interfaces → Security → Repository → Services → API
- Dependency chain is STRICT. No layer references above it.
- Controllers = thin. Services = interfaces. DB = repository layer only.
- Dapper ONLY for: seat capacity, analytics, reports, location aggregates, spatial distance queries (Haversine).
- EF Core for everything else.

---

## Critical Rules

### Roles
- Roles are IMMUTABLE (set at registration).

### Liveness
- Driver liveness required at EVERY login (password and Google OAuth — no exceptions).
- Liveness check runs inside face-api.js WebView. Selfie captured inside the same WebView session, not separately after it.
- Liveness selfie = driver's active profile photo. Replaces previous photo on every successful liveness pass. Deterministic path per driver: `{Storage:DriverPhotoBasePath}/{driverId}/profile.jpg` — no per-session filenames. Base path is config-driven: key `Storage:DriverPhotoBasePath`. Never hardcoded. Passengers see this photo on ride cards.
- Server issues a challenge nonce before liveness. Client must return the nonce with the result. Nonce is short-TTL and single-use. Backend validates nonce before accepting any liveness result. TTL is config-driven: key `Liveness:NonceTtlMinutes` (default 2). Overridable by env var `Liveness__NonceTtlMinutes`.
- Fail counter is server-side only. Counter increments inside the liveness submission handler. No separate report-failure endpoint exists. Client never controls its own penalty.
- `LivenessVerified` in DB is a historical flag only — records that the driver has completed liveness at least once. It does NOT bypass liveness on subsequent logins. Current session liveness is determined by the `livenessPending` JWT claim only.
- Partial driver token (issued after credential check, before liveness) carries claim `livenessPending = true`. Full driver token (issued after liveness pass) does NOT carry this claim. Liveness endpoints require `RequiresLivenessPending` policy — Driver role + `livenessPending = true`. A fully authenticated driver cannot re-hit liveness endpoints.
- The only place a full driver token is issued is inside the liveness success handler. Every driver auth path (password login, password registration, Google OAuth login, Google OAuth registration) ends with a partial token. There are no exceptions.

### Mid-Ride Token Refresh
- Driver refresh token may be renewed while an active ride exists (`Ride.Status = Posted or Active`). Once the ride completes, the next refresh within the same token session is denied and the driver must re-authenticate with liveness.
- "Same token session" means `ride.CompletedAt >= token.iat` — the ride completed after the current access token was issued. If the ride completed before the current token was issued, the refresh is allowed normally. The `iat` claim from the incoming access token is the session boundary — not the refresh token's creation time.
- Mid-ride token expiry is handled silently by Flutter via background refresh. Driver is never logged out during an active ride.

### Login Fail Counter
- Login fail counter lives on the `Users` table: columns `LoginFailCount` (int, default 0, not null) and `LoginBannedUntil` (DateTime UTC, nullable). Applies to all roles equally. This counter is completely independent of `LivenessFailCount` — they track different things and must never affect each other.
- 10 consecutive login failures trigger a 15-minute account lockout. Config keys: `Auth:LoginLockoutThreshold` (default 10), `Auth:LoginLockoutMinutes` (default 15). Both overridable by env vars `Auth__LoginLockoutThreshold` and `Auth__LoginLockoutMinutes`. Zero hardcoded values in code.
- Counter resets on successful credential verification, before liveness. Resetting the login counter does not affect `LivenessFailCount` or the liveness flow.
- When the account does not exist (user not found by email or OAuth lookup), the counter is not incremented — there is no row to update. The response is still `"Invalid credentials."` Lockout applies per existing account only.
- When a credential check fails for an existing account (wrong password, invalid Google token): call `IncrementLoginFailAsync(userId)`, re-fetch `LoginFailCount`, apply lockout if threshold reached.
- Every login path checks `LoginBannedUntil` at the very top before any credential check. If set and in the future: return `ACCOUNT_LOCKED` immediately. Stop. Do not proceed.

### Login Response Rules (Enumeration Protection)
- All credential-check failure responses (wrong password, user not found, invalid Google token) return the exact same response body and message: `"Invalid credentials."` No field, status, timing, or structure may distinguish between them. This is non-negotiable — username enumeration is forbidden. This rule applies to credential-check failures only (wrong password, user not found, invalid Google token). The `ACCOUNT_LOCKED` response is a separate error code with its own message and does not violate this rule.
- `ACCOUNT_LOCKED` is a distinct error code returned when the per-account lockout threshold is reached. It is not a credential-check failure. Its message is `"Too many failed login attempts. Try again later."` and its response body includes `bannedUntil` (DateTime UTC). This does not violate the enumeration-protection rule because it reveals no information about whether the password was correct — it only reveals that the account has been locked due to repeated failures.
- Response shape for `ACCOUNT_LOCKED`: `{ statusCode: 401, error: "ACCOUNT_LOCKED", message: "Too many failed login attempts. Try again later.", bannedUntil: <DateTime UTC> }`.

### SignalR Hub Throttling
- ASP.NET Core middleware rate limits do not reach SignalR hub methods. Throttling must live inside the hub layer.
- All hubs inherit from `ThrottledHub` (in `PakiCarpool.Common`). Hub throttle is per-user per-bucket, tracked in a static `ConcurrentDictionary`, in-memory only. State resets on server restart — known limitation, acceptable until horizontal scaling is required.
- `UpdateLocation` hub method: config key `HubThrottle:LocationPerMinute` (default 15) calls/min per driver. Overridable by env var `HubThrottle__LocationPerMinute`. Silent drop on exceed — no error sent, no disconnect.
- Chat send hub method: config key `HubThrottle:ChatPerMinute` (default 20) calls/min per user. Overridable by env var `HubThrottle__ChatPerMinute`. Silent drop on exceed — no error sent, no disconnect.
- On throttle limit reached: return immediately from the hub method. No error sent to client. No disconnect. No warn-level log entry.

### Ride and Booking Rules
- Driver cannot post if any Ride is `Posted` or `Active`.
- Passenger max ONE active booking.
- Seat capacity = PER SEGMENT (Dapper query), not total ride.
- Chat scoped to ONE booking only. Active status only. 7-day expiry.

### Admin Rules
- Admin accounts SEEDED only. No public admin register.
- Admin JWT = 10 min, NO refresh token. Admin is genuinely logged out at expiry and must re-authenticate with TOTP.
- Admin app shows visible countdown when less than 3 minutes remain. Format: `"Session expires in X:XX"`. Calculated from the token's `exp` claim on every UI tick.
- On admin token expiry or any admin 401: block all further API calls immediately, surface a clear message that any in-progress action did not complete, route to TOTP login screen. The admin must manually retry any action after re-authenticating. Message: `"Your session has expired. Please re-authenticate."`

### Ride Stop State
- `RideStop.Status` is the single source of truth for stop state. The `IsCompleted` boolean has been removed entirely — no reference to it may exist anywhere in the codebase.
- Valid stop statuses: `Upcoming` (valid pickup/dropoff candidate), `Active` (driver travelling toward this stop), `Completed` (driver arrived — no longer a valid pickup or dropoff).
- Only `Upcoming` stops are offered as pickup or dropoff candidates in search and booking validation. `Active` and `Completed` stops are rejected.
- `ArrivedAt` (DateTime UTC, nullable) is still set when the driver arrives. It coexists with `Status = Completed` — they are not competing sources of truth.

### Ride Search
- Ride search filters at stop level, not ride level. A ride appears in results if it has at least one `Upcoming` stop within radius of the passenger's start and at least one `Upcoming` stop within radius of the passenger's end, with end `SequenceOrder` strictly greater than start `SequenceOrder`.
- Perfect match = stop within 50 metres of passenger's input (GPS tolerance). Closest = stop within passenger's chosen radius but outside 50 metres. Outside radius = excluded from results entirely.
- Passenger sets search radius per search (slider: 100m minimum, 5000m maximum, 500m default). Last used radius is stored locally in Flutter and restored on next session.
- Search results are ranked: Rank 1 (both stops perfect match) → Rank 2 (one perfect, one closest) → Rank 3 (both closest). Within the same rank, sorted by combined distance ascending.
- Distance label is shown on result cards for any stop that is not a perfect match. Perfect match stops show no distance label. Stop name is always shown.
- Haversine distance calculations run in PostgreSQL via Dapper — not in application memory.
- All search config values are config-driven. Keys: `RideSearch:PerfectMatchToleranceMeters` (default 50), `RideSearch:DefaultRadiusMeters` (default 500), `RideSearch:MinRadiusMeters` (default 100), `RideSearch:MaxRadiusMeters` (default 5000). All overridable by env vars with double-underscore separator (e.g. `RideSearch__PerfectMatchToleranceMeters`). Zero hardcoded values.

### Address Autocomplete
- Address autocomplete is provided by self-hosted Photon, run as a systemd service directly on the host (Java, no Docker/WSL2), using the pre-built Asia search index downloaded from `download1.graphhopper.com`. Photon binds to `127.0.0.1` only. All autocomplete requests are proxied through the ASP.NET Core API — Photon is never exposed directly. No external geocoding API is used.
- Photon base URL is config-driven: key `Photon:BaseUrl`. Never hardcoded.

---

## Token Lifetimes
- Passenger access token: 30 minutes. Silent background refresh when less than 5 minutes remain.
- Driver access token: 15 minutes. Silent background refresh when less than 5 minutes remain.
- Admin access token: 10 minutes. No refresh token. Genuine logout at expiry.
- Refresh token (Passenger and Driver only): 7 days, httpOnly Secure SameSite=Strict cookie.
- Token lifetime values are config-driven. Keys: `Auth:PassengerTokenExpiryMinutes` (default 30), `Auth:DriverTokenExpiryMinutes` (default 15), `Auth:AdminTokenExpiryMinutes` (default 10). All overridable by env vars `Auth__PassengerTokenExpiryMinutes`, `Auth__DriverTokenExpiryMinutes`, `Auth__AdminTokenExpiryMinutes`. Zero hardcoded expiry values anywhere in code.
- Flutter refreshes based on time remaining (`tokenExpiresAt - now < 5 minutes`), not fixed intervals. Check fires on: app resume (`AppLifecycleState.resumed`), before every outgoing API call, and on a 4-minute periodic timer. The timer only triggers a refresh if the 5-minute condition is met — it is a safety net, not a guaranteed tick.
- On `401 LIVENESS_REQUIRED` from the refresh endpoint: stop immediately, do not retry, clear the access token, discard all queued or pending API calls, route the driver to the liveness screen. Never retry a `LIVENESS_REQUIRED` 401.
- On any other `401` from the refresh endpoint: clear both the access token and the refresh cookie, route to the login screen. Message: `"Your session has expired. Please log in again."`
- SignalR connection is NOT tied to token expiry. Hub authenticated at handshake. Do not reconnect the hub on token refresh. Only reconnect on network drop, following the existing hub reconnect pattern.

---

## Security
- BCrypt work factor 12. AES-256 for license plate and TOTP secrets.
- JWT HS256, secret from env. Refresh token = httpOnly Secure SameSite=Strict cookie.
- Rate limits:
  - General: 100 requests/min (IP-keyed).
  - Auth: 10 requests/min (IP-keyed). 5 auth failures = 15-min IP lockout.
  - Liveness: 5 requests/hour per driver (userId-keyed, not IP). Key `RateLimit:Liveness:Limit` and `RateLimit:Liveness:Period`. Applies to both `GET /api/v1/driver/liveness/challenge` and `POST /api/v1/driver/liveness`. Both endpoints explicitly excluded from the general 100/min bucket.
  - Location (if REST endpoint exists): 30 requests/min (userId-keyed, not IP). Key `RateLimit:Location:PerMinute`. Excluded from general bucket.
  - Account-level login lockout: 10 failures = 15-min lockout (tracked in DB on `Users` table, applies to all roles, userId-keyed). Config keys: `Auth:LoginLockoutThreshold`, `Auth:LoginLockoutMinutes`.
- CORS exact origins only. No wildcards. HTTPS/HSTS mandatory.
- Zero hardcoded secrets. All config via env vars.

---

## Standards
- async/await only. No `.Result`/`.Wait()` anywhere.
- Response format: `{ data, message }` for success, `{ statusCode, error, message }` for errors.
- No stack traces to client. All exceptions caught and mapped to `ApiErrorResponse`.
- No TODOs. No placeholders.
- XML docs on all public service methods.
- Code First only. Entity changes go in Domain, then migrate via EF Core CLI. Migrations must include data migrations where column semantics change (e.g. boolean-to-enum conversions).
- When storing enum values in the database, store as string (not int) so migrations are readable and future enum values are self-documenting.

---

## Config Key Reference

All config keys and their defaults in one place. Every value is overridable by the corresponding env var using double-underscore as separator (e.g. `Auth:LoginLockoutThreshold` → `Auth__LoginLockoutThreshold`).

| Key | Default | Purpose |
|-----|---------|---------|
| `Auth:PassengerTokenExpiryMinutes` | 30 | Passenger JWT lifetime |
| `Auth:DriverTokenExpiryMinutes` | 15 | Driver JWT lifetime |
| `Auth:AdminTokenExpiryMinutes` | 10 | Admin JWT lifetime |
| `Auth:LoginLockoutThreshold` | 10 | Login failures before account lockout |
| `Auth:LoginLockoutMinutes` | 15 | Account lockout duration |
| `Liveness:NonceTtlMinutes` | 2 | Challenge nonce TTL |
| `Storage:DriverPhotoBasePath` | `uploads/drivers` | Base path for driver profile photos |
| `HubThrottle:LocationPerMinute` | 15 | Max UpdateLocation calls/min per driver |
| `HubThrottle:ChatPerMinute` | 20 | Max chat send calls/min per user |
| `RideSearch:PerfectMatchToleranceMeters` | 50 | GPS tolerance for perfect match |
| `RideSearch:DefaultRadiusMeters` | 500 | Default passenger search radius |
| `RideSearch:MinRadiusMeters` | 100 | Minimum allowed search radius |
| `RideSearch:MaxRadiusMeters` | 5000 | Maximum allowed search radius |
| `RateLimit:Liveness:Limit` | 5 | Liveness requests per period |
| `RateLimit:Liveness:Period` | 1h | Liveness rate limit window |
| `RateLimit:Location:PerMinute` | 30 | Location REST endpoint requests/min |
| `Photon:BaseUrl` | — | Photon service base URL (no default — must be set) |

---

## Forbidden

- Hardcoded secrets in `appsettings.json`.
- DbContext calls in Services — DB access goes through the repository layer only.
- In-memory storage for production state.
- Third-party paid APIs. Allowed exceptions: OSM for map display, Google Maps URL for navigation, self-hosted Photon for address autocomplete. No external geocoding APIs.
- Client-reported liveness verdict without a valid server-issued nonce.
- Admin manual liveness approval endpoint (`PUT /admin/drivers/{driverId}/manual-verify`) — removed from v1.0.
- `report-failure` endpoint or any client-callable penalty endpoint.
- Skipping liveness because `LivenessVerified = true` in DB.
- Issuing a full driver token without a completed liveness pass in the current session.
- Any reference to `IsCompleted` on `RideStop` anywhere in the codebase — the field has been removed.
- Distinguishing between "user not found" and "wrong password" in any credential-check failure response.
- Locking or unlocking accounts based on IP alone — account lockout is always userId-keyed.
- Disconnecting a SignalR client as a rate limit response — throttled hub methods drop silently, no error, no disconnect.
- Hardcoded token expiry values anywhere in code — all token lifetimes come from `appsettings`.
- Hardcoded nonce TTL, photo base path, hub throttle limits, search config values, or rate limit values anywhere in code — all come from `appsettings` via the keys in the Config Key Reference table above.
- Retrying a refresh request after receiving `401 LIVENESS_REQUIRED`.
- Reconnecting the SignalR hub on token refresh — hub lifecycle is independent of token lifecycle.
- Adding a refresh token to admin under any circumstance.
- Exposing Photon directly to the internet — Photon binds to `127.0.0.1` only.
- Performing Haversine calculations in application memory — spatial distance queries run in PostgreSQL via Dapper.
- Offering `Active` or `Completed` stops as pickup or dropoff candidates.
- Re-sorting search results on the Flutter client — results are pre-sorted by the backend and displayed as returned.
- Hardcoded radius values, GPS tolerance values, or search config anywhere in code.
- Building or maintaining a custom Photon search index via `-import-file` on raw `.osm.pbf` data — use the pre-built Asia index only.
- Running Photon in Docker or WSL2 — run it as a native systemd service on the host.
- Resetting `LivenessFailCount` when `LoginFailCount` changes, or vice versa — these counters are fully independent.
- Mixing the `iat` claim source: the session boundary for mid-ride refresh denial is always the incoming access token's `iat` claim, never the refresh token's creation timestamp.