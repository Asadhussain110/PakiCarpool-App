# Paki Carpool — Guardrails

## Architecture (NEVER BREAK)
- 7 projects: Domain → Common → Interfaces → Security → Repository → Services → API
- Dependency chain is STRICT. No layer references above it.
- Controllers = thin. Services = interfaces. DB = repository layer only.
- Dapper ONLY for: seat capacity, analytics, reports, location aggregates.
- EF Core for everything else.

## Critical Rules
- Roles are IMMUTABLE (set at registration).
- Driver liveness required at EVERY login.
- Driver cannot post if any Ride is Posted/Active.
- Passenger max ONE active booking.
- Seat capacity = PER SEGMENT (Dapper query), not total ride.
- Chat scoped to ONE booking only. Active status only. 7-day expiry.
- Admin accounts SEEDED only. No public admin register.
- Admin JWT = 10 min, NO refresh token.

## Security
- BCrypt wf 12. AES-256 for license plate & TOTP secrets.
- JWT HS256, secret from env. Refresh token = httpOnly Secure SameSite=Strict.
- Rate limit: 100/min general, 10/min auth, 5 fails = 15min lockout.
- CORS exact origins only. No wildcards. HTTPS/HSTS mandatory.
- Zero hardcoded secrets. All config via env vars.

## Standards
- async/await only. No .Result/.Wait().
- Response format: { data, message } or { statusCode, error, message }.
- No stack traces to client. No TODOs. No placeholders.
- XML docs on all public service methods.

## Forbidden
- Hardcoded secrets in appsettings.json.
- DbContext calls in services.
- In-memory storage for production.
- Third-party paid APIs (OSM + Google Maps URL only).