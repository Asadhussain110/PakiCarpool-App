# PakiCarpool Mobile Frontend Specification

Version: 1.0 draft  
Product sources: supplied FRD, FSD, PRD, SDD, TSD, and Guardrails  
Implementation target: Flutter, Android and iOS; one Passenger/Driver app and a separate Admin app

## 1. Product and Navigation

PakiCarpool is a Karachi carpooling product, not point-to-point ride-hailing. Route Atlas makes ordered stops and per-segment capacity visible. Use native platform navigation: Material 3 patterns on Android and iOS-native navigation, sheets, and pickers on iOS. Keep the same Route Atlas tokens and product flows on both.

Passenger/Driver app navigation:
- Guest: Welcome -> choose Passenger or Driver -> Register / Login.
- Passenger: Rides -> Ride Detail -> Booking Pending -> Active Booking -> Cash/Completion -> Rate or Skip. Profile and booking-scoped chat are secondary destinations.
- Driver: Home/Readiness -> Liveness when required -> Car Registration/Status -> Post Ride -> Booking Requests -> Active Ride -> Cash/Completion -> Rate or Skip.
- Do not add an always-visible notification inbox. Surface live events in context and through the platform notification mechanism only if the push-policy conflict is resolved for v1.

Admin app navigation:
- Admin Login -> TOTP -> Dashboard -> Approvals / Users / Rides / Flags / Reports.
- Session is memory-only and ends when the app closes. Admin has no public registration and no refresh token.

## 2. Shared Design and Interaction System

Visual direction: Route Atlas. Palette roles: near-white `#F5F8F6` background, deep ink `#1C2B30` primary text, canal teal `#087D85` primary/action/route, signal saffron `#E7C64A` pickup and attention, coral `#C14F45` destructive/error, muted gray for passed/completed/inactive. Android maps roles into Material 3 color schemes; iOS uses semantic surfaces with the same brand tint. Barlow is reserved for short route/display headings; use native text styles for body and controls, Dynamic Type, and tabular numerals for fares/countdowns.

Core reusable UI:
- Stop row/chip: sequence number, stop name, status (available, filling, full, passed, current, completed), optional ETA.
- Route strip/map: ordered stop markers, route line, current-driver marker; legend and accessible text summary. Map is `flutter_map` with OSM tiles, not a Leaflet WebView.
- Fare/seat summary: fare per seat, selected segment, seats or capacity only when the API supplies the value.
- Status badge: Pending, Accepted, Rejected, Active, Completed, Cancelled, Approved, Suspended; color is never the only status cue.
- Primary, secondary, destructive, and icon buttons use native 48dp Android / 44pt iOS minimum targets. Include labels/tooltips for unfamiliar icons.
- Forms show field-level validation, preserve user input after recoverable errors, disable duplicate submissions, and expose loading/success/error states.
- Confirm irreversible actions (cancel ride, block user, admin ride cancel). Use reason sheets with the exact API constraints below.
- Shared states: initial loading, refresh, empty result, offline, request failure/retry, expired session, permission denied, reconnecting, and successful completion.

## 3. Guest and Authentication Screens

### A01. Welcome and Role Selection

Audience: Guest. Entry: app launch without a session.  
Display: PakiCarpool name; concise shared-route proposition; Passenger and Driver role choices. Do not imply that a Guest can browse rides because ride APIs require Passenger auth.  
Actions: Continue as Passenger, Continue as Driver, Admin sign-in entry only inside the separate Admin app.  
Rules: selected role is immutable once registration succeeds. Admin is not a public role-selection option.

### A02. Registration

Audience: Passenger or Driver. Fields: full name (2-100 chars), email, password (8-128 chars), confirm password, selected role. Google sign-up button starts OAuth PKCE with the chosen role.  
Actions: Create account, Google sign-up, back to login. On email/password success, move to OTP verification.  
Validation: email format; password match; FRD requires uppercase, lowercase, number, and symbol. Current DTO only validates length; log backend mismatch and do not claim composition is enforced until fixed.  
API: `POST /api/v1/auth/register` with `RegisterRequestDto`; `GET /api/v1/auth/google?role=Passenger|Driver` for OAuth start. Admin registration is rejected by service.

### A03. Email OTP Verification

Fields: email context, six-digit segmented numeric code. Display resend countdown (FSD: 60 seconds) and attempt count (FSD: maximum three attempts).  
Actions: Verify, resend when available, change email/back. Success goes to Login per FSD.  
API: `POST /api/v1/auth/verify-email` with email and OTP. No resend endpoint or attempt limit is currently exposed; those controls must be marked unavailable until backend support exists. OTP expires after ten minutes per current service configuration; reconcile against FSD attempt requirements.

### A04. Passenger/Driver Login

Fields: email, password; hCaptcha area appears after three failed attempts. Actions: Sign in, Continue with Google, Forgot password, Register.  
States: incorrect credentials; email not verified; CAPTCHA required/invalid; 15-minute lockout countdown after five failures; suspended account; network error.  
API: `POST /api/v1/auth/login`. Passenger receives full access token. Driver may receive `LivenessPending=true` and a partial token, which routes to A06.  
Token rules: store Passenger/Driver access JWT with `flutter_secure_storage` per TSD; preserve/rotate the refresh token through the HTTP-only Secure SameSite cookie using a native cookie jar. Refresh before expiry; on refresh failure clear session and return to login. Confirm native cookie behavior against deployed API before declaring refresh complete.

### A05. Forgot and Reset Password

Forgot fields: email. Action: Send reset link; show neutral confirmation to avoid account enumeration. Reset fields: email, emailed token/deep-link token, new password, confirm password. Enforce FRD complexity in client and server; show password requirements inline.  
API: `POST /api/v1/auth/forgot-password`, `POST /api/v1/auth/reset-password`. Expired/invalid token and network failures retain a recovery path to request a new link.

### A06. Driver Liveness

Entry: immediately after Driver registration verification and after every Driver credential login, per FRD/FSD/PRD.  
Display: camera preview, permission rationale, one randomized instruction at a time (blink, turn left, turn right, smile), ten-second challenge countdown, progress/attempt state, privacy explanation, capture state. Selfie is captured with Flutter camera after WebView `LIVENESS_PASS`; do not claim a selfie was captured by face-api.js.  
Actions: Start, retry, open settings if permission denied, continue only after pass. After five consecutive failures, show the 24-hour retry boundary and support/admin-review state.  
API: `POST /api/v1/driver/liveness` with `LivenessResultDto` (`LivenessResult`, `SelfieBase64`); `POST /api/v1/driver/liveness/report-failure` on failed attempt. Success returns a full access token replacing the partial liveness token.  
OPEN: `AuthService` only challenges when `LivenessVerified` is false; it does not challenge again on every later login. Do not change backend liveness behavior in this pass; keep this mismatch in every implementation handoff. GPS location updates are separate and use SignalR every five seconds during a ride.

### A07. Admin Login and TOTP

Login fields: email, password. Second step: six-digit TOTP, 30-second display countdown; allow the documented tolerance window.  
Actions: Sign in, Verify code, retry/back to credentials. Errors: invalid credentials, invalid/expired/replayed code, locked account, network. No Google OAuth, public registration, refresh, or persistent session.  
API: `POST /api/v1/auth/admin/login`; then `POST /api/v1/auth/admin/totp-verify` with code and partial token. Keep final access token in memory only; clear on app close/sign-out.  
Backend contract check: Admin token expiry is 10 minutes, not the Passenger/Driver 15 minutes.

## 4. Passenger Screens

### P01. Ride Listing / Home

Entry: immediately after Passenger login. If an active booking exists, pin an `Active ride` banner above results and route it to P04.  
Ride row displays only available API data: driver name, rating, car make/model, fare, max seats, ride status, ordered stops, stop completion, and ETA when returned. FRD also requires driver photo, available seats, per-stop ETA, passed/full styling, and sorting by ETA; these fields are missing or incomplete in current list responses and are API gaps, not fabricated values.  
Actions: open detail, pull to refresh, paginate. Do not invent search filters; current endpoint accepts pagination only.  
API: `GET /api/v1/passenger/rides?page=&pageSize=`. Envelope: `{ data, message }`; failures use `{ statusCode, error, message }`.

### P02. Ride Detail and Segment Selection

Display: driver identity/rating, car, fare, ordered stops with map and availability state; show passed/full explanation.  
Fields: pickup stop; drop-off stop. Pickup must be a future selectable stop; drop-off must follow pickup. Selection is from this ride's stop IDs, not free text.  
Actions: Book Me (enabled only after valid selection and no active booking), return to listing, open More Info if rating-history API becomes available. Tapping passed/full stops explains the reason.  
API: `GET /api/v1/passenger/rides/{rideId}` then `POST /api/v1/passenger/bookings` with `RideId`, `PickupStopId`, `DropoffStopId`. Backend enforces passed stops, one active booking, and per-segment capacity. Current API does not expose a segment-availability query; don't calculate `Full` from total ride seats.

### P03. Booking Pending / Outcome

Display: `Waiting for driver to accept`, two-minute countdown, selected route segment, fare, cancel/expiry status.  
Actions: wait; open booking after accepted; return to results if rejected, cancelled, or timed out. Prevent duplicate booking taps.  
Events: `BookingAccepted`, `BookingRejected`, `BookingAutoCancelled`; provide REST re-fetch/reconnect fallback. Booking request received by Driver must display passenger, pickup/drop-off, and a two-minute timer.

### P04. Active Booking / Live Tracking

Display: full-screen OSM map; driver marker; pickup and drop-off markers; ordered route; bottom sheet with driver, car, fare, booking status, current stop/status text, and ETA in seconds. Show `last updated`/reconnecting state if location stalls.  
Actions: Chat, Cancel booking (reason required), Driver no-show only after the documented three-minute wait. Cancellation is permitted only before arrival at pickup.  
API: `GET /api/v1/passenger/bookings/active`; `DELETE /api/v1/passenger/bookings/{bookingId}` with 10-500 character reason; `PUT /api/v1/passenger/bookings/{bookingId}/no-show`. Active booking DTO contains driver lat/lng and ETA when available.  
SignalR: connect with bearer token; call `JoinRideGroup(rideId)` and where accepted `JoinPassengerRideGroup(rideId, passengerId)`. Subscribe to location events and `DriverArrivedAtStop`, `RideEndedForPassenger`, `RideCancelledByDriver`; exact event payloads must be contract-tested.

### P05. Cash and Completion

After driver selects End Ride for this booking, show fare, driver name, and `Please pay cash to driver`. Do not collect card/bank details. On `CashReceivedConfirmed`, close completion state and open P06.  
Events: `RideEndedForPassenger(bookingId, fare)`, `CashReceivedConfirmed(bookingId)`. Re-fetch active booking after reconnect rather than assuming delivery.

### P06. Rate Driver / Flag Driver

Rating: 1-5 stars, optional review up to 300 characters, Submit and Skip. Both close the opportunity; Skip is final.  
Flag: reason required, 10-500 characters, confirmation after submit.  
API: `POST /api/v1/passenger/ratings` with ride/ratee IDs, stars, review; `POST /api/v1/passenger/flags` with reportee/ride IDs and reason. Profile-wide rating history is required by FRD/FSD but no read endpoint is present.

## 5. Driver Screens

### D01. Driver Home / Readiness

Display readiness gates in order: liveness status, car status, active ride. Show next action prominently. If car is pending/rejected, explain why ride posting is unavailable. If a Posted/Active ride exists, resume its dashboard; don't offer a second ride.  
Actions: complete liveness, add/update car, post ride only when eligible, resume active ride, profile/logout. Do not display personal trust-score detail unless an API provides it.

### D02. Car Registration / Update

Fields: make (max 50), model (max 50), year (2000-2030), license plate (max 20), available seats (1-6), exterior photos (FRD/FSD minimum two), registration-document photo (required).  
Actions: take/select photos, preview/remove before submit, submit, retry failed upload. Show privacy-protected plate in ordinary UI.  
Workflow: upload binary evidence first through the required authenticated upload API; then submit returned storage references to `POST /api/v1/driver/car`. Current backend only accepts `PhotoPaths` and `RegDocPath` strings; there is no upload route. Do not send local paths, arbitrary remote paths, or fake upload success.  
Update: `PUT /api/v1/driver/car/{carId}`. FRD says old approved car remains active pending approval of the replacement; current `UpdateCarAsync` resets the same car to Pending. Keep the update workflow blocked until backend contract is corrected or clearly label the consequence.

### D03. Car Status

Display each car's make/model/year/status, rejection reason, approved date; actionable next step for Pending, Approved, or Rejected.  
API: `GET /api/v1/driver/car/status`. Current `CarStatusResponseDto` does not include plate, photo paths, or document paths. Rejection reason is available. Driver cannot post before one car is Approved.

### D04. Post Ride

Preconditions: liveness accepted, approved car, no Posted/Active ride.  
Stop builder: at least two stops; each has name (max 200), map pin/manual latitude and longitude, 1-based sequence. Add, remove, reorder before submission; preview route and stop order. Stop sequence follows travel direction.  
Fields: fare per seat (positive, current API max 100000), max seats (1-6 and no more than approved car capacity).  
Actions: Add Stop, Pin on Map, reorder, preview, Post Ride, confirm success. Cancellation grace timer begins after post.  
API: `POST /api/v1/driver/rides` with stops/fare/max seats. No departure-time field exists and PRD excludes scheduled/recurring rides, so no departure control in v1.

### D05. Booking Requests

Cards: passenger name, pickup/drop-off stop, request time/status, two-minute accept deadline. Rating is a required FSD field but current `BookingDetailDto` contains no passenger rating; omit until API provides it. Expired/rejected requests become non-actionable.  
Actions: Accept, Reject. Accept revalidates segment capacity; full segments can be rejected by backend. Confirm reject to avoid accidental denial.  
API: `GET /api/v1/driver/rides/{rideId}/bookings`, `PUT /api/v1/driver/bookings/{bookingId}/accept`, `/reject`.

### D06. Active Ride Dashboard

Top: route progress A->B->C, current stop, next-stop navigation. Map/route area stays secondary to safe, glanceable action controls. Booking list: passenger name, pickup/drop-off, status, requested/accepted times, cash-paid state. Current DTO has no per-booking fare or passenger rating.  
Per booking actions: I Am Here before pickup; End Ride after drop-off; Cash Received only after End Ride; Mark Passenger No-Show only after three-minute wait; Chat only for accepted/active booking.  
Per stop actions: Arrived at [Stop], Navigate to [Next Stop]. On arrive, passed stops become unavailable, affected passengers are notified, and route navigation updates.  
Location: request foreground/background location permission as needed; send `UpdateLocation(rideId, lat, lng)` over SignalR every five seconds while ride is active. Stop on completed/cancelled. Show permission, GPS unavailable, reconnect, and stale-location states.  
API: `GET /api/v1/driver/rides/active`, booking list above, `PUT /api/v1/driver/stops/{stopId}/arrive`, per-booking `/iam-here`, `/end-ride`, `/cash-received`, `/no-show`; ride cancel is `DELETE /api/v1/driver/rides/{rideId}` with reason.

### D07. Cancel Ride

Show affected accepted passengers and cancellation impact. Reason required after grace period by FRD/FSD; current `CancelRideDto` requires a 10-500 character reason at all times. Until backend supports optional-within-grace semantics, collect the reason for every cancellation so requests validate, but clearly note the discrepancy in client contract tests. Confirm before submit. API: `DELETE /api/v1/driver/rides/{rideId}`.

### D08. Rate Passenger / Flag Passenger

Same 1-5 stars, optional 300-character review, final Skip; flag reason 10-500 characters. Entry follows per-passenger cash completion. API: `POST /api/v1/driver/ratings`, `POST /api/v1/driver/flags`.

## 6. Shared Chat

### C01. Booking Chat

One chat per Passenger-Driver booking; no cross-booking or group messaging. Text only, max 1000 chars, seven-day history. UI: app bar with counterpart and ride segment; left/right bubbles; timestamp; composer/send; unread indicator; Connected/Reconnecting state.  
Availability: only for an accepted/Active booking; hide/disable before acceptance and after completion. FSD says Active status; backend service behavior must be verified because `BookingStatus` includes Accepted and Active.  
History: `GET /api/v1/chat/{bookingId}/messages`. Real-time: SignalR `SendChatMessage(bookingId, receiverId, message)`; queue failed sends in memory and silently retry on reconnect. Do not promise persistence beyond server's seven-day expiry. Validate ownership and sender/receiver against the booking.

## 7. Admin Screens

### M01. Admin Dashboard

Display current API metrics: total users, total drivers, total passengers, total rides, active rides, pending car approvals, open flags, suspended accounts. Quick actions: approvals, flags, users. FSD also requires recent activity (registrations, approvals, flags, cancellations); current API and NotificationController have no activity-feed read route. Add only after endpoint exists; do not synthesize historical activity from live notifications.

### M02. User List and M03. User Detail

List: paginated name, email, role, active/suspended, trust score, created date. Search/filter by role/status are FSD requirements but current `GET /api/v1/admin/users` only accepts pagination. Do not display rating in list unless supplied.  
Detail: identity, verification, account state, trust score, creation date; Driver profile adds liveness state/failure count, profile photo, average rating, cars and approval evidence. FSD additionally requires ride, rating, cancellation, and flags histories; current `UserDetailDto` lacks these histories and passenger profile detail.  
Actions: Block with required 10-500 character reason; Unblock with confirmation. Admin accounts cannot be blocked via current service. API: `GET /admin/users`, `GET /admin/users/{userId}`, `PUT /admin/users/{userId}/block`, `PUT /admin/users/{userId}/unblock` under `/api/v1`.

### M04. Driver Approvals

Queue pending vehicles; display driver, make/model/year, seats, submission time, car photos, registration document, status. Separate liveness-failure queue/alert with selfie and manual verification.  
Actions: Approve; Reject with reason (API requires 10-500 chars though FSD says optional); Manual Verify. Confirm actions and show result/notification state.  
API: `GET /api/v1/admin/driver-approvals`, approve/reject routes under `/driver-approvals/{carId}`, `PUT /api/v1/admin/drivers/{driverId}/manual-verify`. Current upload/read access for stored files is not defined and is a blocking readiness gap.

### M05. Ride Management

Paginated ride list with driver, status, fare, max seats, route stops; FSD wants status filters, but current endpoint supports pagination only. Detail should include stop order, bookings/passengers, fare, statuses, and timeline; no dedicated admin ride-detail endpoint currently exists, and list DTOs omit booking/timeline detail.  
Action: Cancel active ride with a confirmation and downstream notification. API: `GET /api/v1/admin/rides`, `DELETE /api/v1/admin/rides/{rideId}`.

### M06. Flags

Paginated list: reporter, reportee, reason, ride reference, status, creation time. Detail shows available context; full history is not currently returned.  
Actions: Dismiss with optional notes (max 500); Action with 10-500 character action text. UI clarifies that backend text containing block/suspend triggers account blocking; otherwise it sends a warning. Prefer a typed action selector only after API contract is made structured.  
API: `GET /api/v1/admin/flags`, dismiss/action routes by flag ID.

### M07. Reports

Date range picker; reports listed in FSD: rides summary, user activity, cancellations, ratings overview, flags. FRD requires total bookings and average rating, with user-type and ride-status filters. Current API accepts only `from`/`to`, returns dashboard summary plus a limited ride sample and counts, and export supports CSV/PDF. Show only actual response fields until report contracts are expanded; expose unsupported report types/filters as API backlog, not dead controls.  
API: `GET /api/v1/admin/reports?from=&to=`; `GET /api/v1/admin/reports/export?from=&to=&format=csv|pdf`. Handle file name/content type, empty report, export failure, and share/save destination.

## 8. Notification and Real-Time Contracts

- REST success/error envelope as documented by API; handle 401 refresh, 403 role restriction, 404 missing entity, 409 timeout/liveness/lockout states, 429 rate limit, and server failure without stack traces.
- SignalR hub: `/hubs/pakicarpool`; access-token factory supplies the current access JWT. Connection auto-reconnect uses exponential backoff; refresh auth before reconnect if token expired.
- Hub client methods: `UpdateLocation(rideId, lat, lng)`, `SendChatMessage(bookingId, receiverId, message)`, `JoinRideGroup(rideId)`, `JoinPassengerRideGroup(rideId, passengerId)`, `LeaveRideGroup(rideId)`.
- Server events currently named: `ReceiveNotification`, `ReceivePushNotification` (push simulation, not native push), `BookingAccepted`, `BookingRejected`, `BookingAutoCancelled`, `DriverArrivedAtStop`, `RideEndedForPassenger`, `CashReceivedConfirmed`, `RideCancelledByDriver`, `RideFullNotification`, `LivenessFailureAlert`. Add contract tests for payload casing/shape and exact group membership.
- Notification history: no screen, persistence, API, or inbox in v1. In-app events are transient SignalR; emails follow the notification matrix. Native FCM/APNs is excluded by PRD.

## 9. API Conflict and Readiness Register

| ID | Requirement | Current implementation | Resolution/status |
| --- | --- | --- | --- |
| R-01 | .NET 10 | API targets `net10.0`; SDD/TSD state .NET 8 | Resolved by owner: .NET 10. Correct SDD/TSD references next revision. |
| R-02 | OSM map on native Flutter | FRD/FSD/SDD say Leaflet/OSM; TSD says `flutter_map` | Resolved: `flutter_map` + OSM tiles; Leaflet is source/experience wording, not a WebView library requirement. |
| R-03 | Liveness at registration and every Driver login | AuthService checks persistent `LivenessVerified` and skips subsequent logins | OPEN, explicitly do not change backend during this pass. Keep every-login requirement and mismatch visible. Liveness HTTP submission and SignalR ride GPS are separate flows. |
| R-04 | Required car exterior photos + registration document | Car DTO takes string paths; no upload or controlled retrieval endpoint | Required backend work before driver car approval slice can be called integrated. |
| R-05 | Approved car remains available during replacement review | Current update sets updated car to Pending | Backend change needed; preserve old approved car per FRD. |
| R-06 | FSD OTP resend/three attempts/60-second timer | Verify endpoint exists; resend/attempt constraints absent | Backend contract needed; don't ship fake resend. |
| R-07 | FRD password composition | DTO validates only email/length | Add server validation; client mirrors it after confirmed policy. |
| R-08 | Driver per-login liveness and 24-hour ban UI | Backend issues partial token when not verified and exposes failure report; persistent flag bypasses later checks | UI follows required flow; repeated-login behavior remains OPEN per R-03. |
| R-09 | Driver rating in booking request | Booking DTO omits passenger rating | Backend field needed before rendering the requested rating. |
| R-10 | Driver home/list/route availability fields | Current ride DTO omits driver photo, segment available seats/fullness, and some per-stop ETA; search/filter not exposed | Backend contract additions; never infer segment fullness from total seats. |
| R-11 | Admin activity, profile histories, user search, ride detail, report types/filters | No matching read endpoints or incomplete data | Add API contracts for required FSD/FRD fields before claiming these screens fully integrated. |
| R-12 | Phone notifications | FRD matrix names phone notification; PRD excludes FCM/APNs | v1 uses in-app SignalR and email only; record FRD matrix conflict. |
| R-13 | Fare semantics | FRD says fare per seat; API has ride-level `Fare` | Confirm shared meaning in contract; present as per-seat only if backend applies it consistently. |
| R-14 | Cancel reason only after grace | Current request DTO always requires 10-500 chars | Collect reason until contract is reconciled; do not imply grace-period optionality works. |
| R-15 | Guest ride browsing | Passenger ride API requires Passenger auth | No guest browsing in v1 unless API auth policy changes. |

## 10. Delivery and Acceptance Order

1. First integration slice: Admin TOTP -> Driver registration/email verification -> liveness per documented flow -> real car evidence upload -> Admin review/approval -> Driver posts a route. The liveness mismatch remains open and is not silently considered fixed.
2. Verify this slice against the running API and test the upload/approval contract before building passenger discovery.
3. Passenger listing/detail/segment booking -> two-minute outcome -> live OSM tracking -> booking chat -> cash completion -> rating/flag.
4. Finish Driver stop progression, passenger-specific end/cash/no-show, cancellation, chat/rating/flag; then remaining Admin users/rides/flags/reports.

Acceptance: form constraints match DTOs; every screen has loading/empty/error/offline/permission states; API-backed values are not fabricated; role gates match server authorization; route ordering and per-segment capacity are tested; SignalR reconnect restores current booking/ride state; chat is booking-scoped and bounded to seven days; Android/iOS native navigation, safe areas, text scaling, Reduce Motion, dark mode, and touch target sizes are verified.
