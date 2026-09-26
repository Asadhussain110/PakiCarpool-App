# PAKI CARPOOL — AI CODER MASTER PROMPT

You are an expert senior full-stack engineer. Your job is to build **Paki Carpool**, a production-grade mobile carpooling application for Karachi, Pakistan. Follow every instruction in this prompt exactly. Do not skip sections. Do not use placeholder comments like `// TODO` — implement everything fully.

---

## SYSTEM OVERVIEW

Paki Carpool has three parts:
1. **Passenger/Driver Flutter App** — installable on Android (APK + Play Store) and iOS (App Store)
2. **Admin Flutter App** — separate app on a separate domain, for platform administrators only
3. **ASP.NET Core Web API Backend** — self-hosted, serves both Flutter apps

There are three fixed user roles: **Passenger**, **Driver**, **Admin**. A user cannot hold more than one role. Role is selected at registration and is permanent.

---

## TECH STACK

| Component | Technology |
|---|---|
| Mobile Apps | Flutter (Dart), targeting Android + iOS |
| Backend | ASP.NET Core Web API, .NET 10 |
| Database | PostgreSQL 16 (self-hosted) |
| ORM | EF Core 8 Code First + Dapper (for complex queries) |
| Authentication | ASP.NET Core Identity + JWT Bearer + Google OAuth 2.0 (PKCE) |
| Real-time | SignalR (built into ASP.NET Core) |
| Email | Brevo SMTP (MailKit library) |
| Maps (passenger) | flutter_map package + OpenStreetMap tiles (free, no API key) |
| Navigation (driver) | Google Maps deep-link URL (no API key needed) |
| Liveness Detection | face-api.js running inside Flutter WebView |
| SSL | Let's Encrypt via Certbot |
| Reverse Proxy | Nginx |
| Infrastructure | Oracle Cloud Always Free (self-hosted) |

---

## BACKEND — SOLUTION STRUCTURE

Create a .NET solution named `PakiCarpool.sln` with these projects. Each is a separate Class Library except the API project:

```
PakiCarpool.API           — ASP.NET Core Web API (.NET 8)
PakiCarpool.Interfaces    — Class Library (IService + IRepository declarations only)
PakiCarpool.Security      — Class Library (JWT, BCrypt, rate limiting, encryption)
PakiCarpool.Services      — Class Library (business logic, implements Interfaces)
PakiCarpool.Repository    — Class Library (EF Core DbContext + Dapper queries)
PakiCarpool.Domain        — Class Library (Models, DTOs, Enums — zero dependencies)
PakiCarpool.Common        — Class Library (utilities, extensions, constants)
```

### Dependency Rules (STRICT — never break these)
- API → references Interfaces, Security, Services, Common
- Interfaces → references Domain only
- Security → references Domain, Common only
- Services → references Interfaces, Domain, Security, Common
- Repository → references Domain, Common
- Domain → no references to any project
- Common → no references to any project
- Services and Repository are wired together via DI in API/Program.cs

### Folder Structure inside each project

**PakiCarpool.API**
```
/Controllers
  AuthController.cs
  DriverController.cs
  PassengerController.cs
  ChatController.cs
  AdminController.cs
/Hubs
  PakiCarpoolHub.cs
/Middleware
  ExceptionHandlingMiddleware.cs
  RateLimitingMiddleware.cs
Program.cs
appsettings.json
appsettings.Production.json
```

**PakiCarpool.Interfaces**
```
/IServices
  IAuthService.cs
  IDriverService.cs
  IPassengerService.cs
  IChatService.cs
  IAdminService.cs
  IEmailService.cs
  ILivenessService.cs
  ITrustService.cs
  INotificationService.cs
/IRepositories
  IUserRepository.cs
  IRideRepository.cs
  IBookingRepository.cs
  IChatRepository.cs
  IAdminRepository.cs
```

**PakiCarpool.Security**
```
/Helpers
  JwtHelper.cs
  BCryptHelper.cs
  AesEncryptionHelper.cs
  TotpHelper.cs
/Middleware
  JwtMiddleware.cs
/Policies
  RolePolicy.cs
```

**PakiCarpool.Services**
```
/Implementations
  AuthService.cs
  DriverService.cs
  PassengerService.cs
  ChatService.cs
  AdminService.cs
  EmailService.cs
  LivenessService.cs
  TrustService.cs
  NotificationService.cs
/BackgroundJobs
  BookingTimeoutJob.cs
  ChatExpiryJob.cs
  RefreshTokenCleanupJob.cs
  TrustScoreJob.cs
  RideGraceExpiryJob.cs
```

**PakiCarpool.Repository**
```
/EFCore
  PakiCarpoolDbContext.cs
  /Configurations   (Fluent API configs per entity)
  /Migrations
  /Repositories
    UserRepository.cs
    RideRepository.cs
    BookingRepository.cs
    ChatRepository.cs
    AdminRepository.cs
/Dapper
  SeatCapacityQuery.cs
  RideAnalyticsQuery.cs
  LiveLocationQuery.cs
  ReportQuery.cs
```

**PakiCarpool.Domain**
```
/Models
  User.cs
  DriverProfile.cs
  DriverCar.cs
  Ride.cs
  RideStop.cs
  Booking.cs
  DriverLocation.cs
  ChatMessage.cs
  Rating.cs
  RefreshToken.cs
  TotpSecret.cs
  CancellationLog.cs
  NoShowLog.cs
  Flag.cs
/DTOs
  /Auth
  /Driver
  /Passenger
  /Chat
  /Admin
  /Common
/Enums
  UserRole.cs
  RideStatus.cs
  BookingStatus.cs
  CarStatus.cs
  CancellationClassification.cs
  FlagStatus.cs
  NotificationType.cs
```

---

## DATABASE SCHEMA

Use EF Core Code First. Define all entities in Domain/Models. Apply Fluent API configuration in Repository/EFCore/Configurations.

### Users
```csharp
public class User
{
    public Guid Id { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }      // BCrypt, nullable if Google OAuth
    public UserRole Role { get; set; }            // Enum: Passenger, Driver, Admin
    public string? GoogleId { get; set; }
    public bool IsEmailVerified { get; set; }
    public string? EmailOtp { get; set; }
    public DateTime? EmailOtpExpiry { get; set; }
    public bool IsActive { get; set; }            // false = suspended/blocked
    public bool IsSuspended { get; set; }
    public int TrustScore { get; set; }           // starts at 100
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### DriverProfile
```csharp
public class DriverProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool LivenessVerified { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public int LivenessFailCount { get; set; }
    public DateTime? LivenessBannedUntil { get; set; }
    public decimal AverageRating { get; set; }
    public User User { get; set; }
    public ICollection<DriverCar> Cars { get; set; }
}
```

### DriverCar
```csharp
public class DriverCar
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }            // FK to DriverProfile
    public string Make { get; set; }
    public string Model { get; set; }
    public int Year { get; set; }
    public string LicensePlate { get; set; }      // AES-256 encrypted
    public int AvailableSeats { get; set; }       // 1-6 hard cap
    public CarStatus Status { get; set; }         // Pending, Approved, Rejected
    public string PhotoPaths { get; set; }        // JSON array of paths
    public string RegDocPath { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DriverProfile Driver { get; set; }
}
```

### Ride
```csharp
public class Ride
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }            // FK to DriverProfile
    public Guid CarId { get; set; }               // FK to DriverCar
    public decimal Fare { get; set; }
    public int MaxSeats { get; set; }
    public RideStatus Status { get; set; }        // Posted, Active, Completed, Cancelled
    public DateTime CreatedAt { get; set; }
    public DateTime CancelGraceExpiresAt { get; set; }  // CreatedAt + 5 minutes
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancelReason { get; set; }
    public DriverProfile Driver { get; set; }
    public DriverCar Car { get; set; }
    public ICollection<RideStop> Stops { get; set; }
    public ICollection<Booking> Bookings { get; set; }
}
```

### RideStop
```csharp
public class RideStop
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public string Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int SequenceOrder { get; set; }        // 1-based, enforces travel order
    public bool IsCompleted { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public Ride Ride { get; set; }
}
```

### Booking
```csharp
public class Booking
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public Guid PassengerId { get; set; }         // FK to User
    public Guid PickupStopId { get; set; }        // FK to RideStop
    public Guid DropoffStopId { get; set; }       // FK to RideStop
    public BookingStatus Status { get; set; }     // Pending, Accepted, Rejected, Active, Completed, Cancelled
    public DateTime RequestedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsCashPaid { get; set; }
    public string? CancelReason { get; set; }
    public CancellationClassification? CancelClassification { get; set; }
    public Ride Ride { get; set; }
    public User Passenger { get; set; }
    public RideStop PickupStop { get; set; }
    public RideStop DropoffStop { get; set; }
    public ICollection<ChatMessage> ChatMessages { get; set; }
}
```

### DriverLocation
```csharp
public class DriverLocation
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid RideId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime RecordedAt { get; set; }
    // Upsert pattern: one active record per driver per ride
}
```

### ChatMessage
```csharp
public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }           // Scoped to one booking (one passenger ↔ their driver)
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string Message { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }       // SentAt + 7 days
    public bool IsDelivered { get; set; }
    public Booking Booking { get; set; }
}
```

### Rating
```csharp
public class Rating
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public Guid RaterId { get; set; }
    public Guid RateeId { get; set; }
    public int Stars { get; set; }                // 1-5
    public string? ReviewText { get; set; }       // max 300 chars, optional
    public DateTime CreatedAt { get; set; }
}
```

### RefreshToken
```csharp
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; }             // SHA-256 hashed before storage
    public DateTime ExpiresAt { get; set; }       // 7 days
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    public User User { get; set; }
}
```

### TotpSecret (Admin only)
```csharp
public class TotpSecret
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Secret { get; set; }            // AES-256 encrypted
    public bool IsVerified { get; set; }
    public User User { get; set; }
}
```

### CancellationLog
```csharp
public class CancellationLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid EntityId { get; set; }            // BookingId or RideId
    public string EntityType { get; set; }        // "Booking" or "Ride"
    public string Reason { get; set; }
    public CancellationClassification Classification { get; set; }  // Valid, Frivolous
    public int TrustPointsDeducted { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### NoShowLog
```csharp
public class NoShowLog
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid OffenderId { get; set; }
    public Guid RideId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### Flag
```csharp
public class Flag
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid ReporteeId { get; set; }
    public Guid RideId { get; set; }
    public string Reason { get; set; }
    public FlagStatus Status { get; set; }        // Open, Dismissed, ActionTaken
    public DateTime CreatedAt { get; set; }
}
```

---

## ENUMS

```csharp
public enum UserRole { Passenger, Driver, Admin }
public enum RideStatus { Posted, Active, Completed, Cancelled }
public enum BookingStatus { Pending, Accepted, Rejected, Active, Completed, Cancelled }
public enum CarStatus { Pending, Approved, Rejected }
public enum CancellationClassification { Valid, Frivolous }
public enum FlagStatus { Open, Dismissed, ActionTaken }
```

---

## AUTHENTICATION SYSTEM

### Email/Password Registration
1. Validate request body (full name, email, password, role)
2. Check email not already registered
3. Hash password with BCrypt work factor 12
4. Create User record with IsEmailVerified = false
5. Generate 6-digit OTP, store hashed OTP + expiry (10 minutes) on User record
6. Send OTP via Brevo SMTP (MailKit)
7. If role = Driver: create DriverProfile record
8. If role = Passenger: create PassengerProfile record
9. Return 201 with message "Check your email for verification code"

### Email OTP Verification
1. Validate OTP matches stored hash and is not expired
2. Set IsEmailVerified = true, clear OTP fields
3. Return 200

### Email/Password Login
1. Find user by email
2. Check IsEmailVerified = true, IsActive = true, IsSuspended = false
3. Check LockoutUntil — if locked, return 409 with remaining time
4. Validate BCrypt password
5. On fail: increment FailedLoginCount
   - At 3 failures: require hCaptcha on next attempt
   - At 5 failures: set LockoutUntil = now + 15 minutes, return 409
6. On success: reset FailedLoginCount
7. If role = Driver: trigger liveness check flow (return token with LivenessPending claim)
8. Issue JWT access token (15 min) + refresh token (7 days, stored hashed in DB)
9. Set refresh token as httpOnly, Secure, SameSite=Strict cookie
10. Return 200 with access token in body

### Google OAuth 2.0 (PKCE)
1. GET /api/v1/auth/google → return Google OAuth authorization URL with code_challenge
2. GET /api/v1/auth/google/callback → exchange code for tokens, validate ID token
3. Extract email and Google sub (ID) from ID token
4. If user exists with GoogleId: login flow (step 6 onwards above)
5. If new user: role must have been passed as state parameter → create account
6. Issue JWT pair same as email login

### JWT Configuration
```json
{
  "Jwt": {
    "Issuer": "api.pakicarpool.pk",
    "Audience": "pakicarpool-clients",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7,
    "Secret": "[FROM ENVIRONMENT VARIABLE - NEVER HARDCODE]"
  }
}
```

JWT claims: `userId`, `email`, `role`, `jti` (unique token ID), `livenessPending` (Driver only)

### Token Refresh
- POST /api/v1/auth/refresh reads httpOnly cookie
- Validates token is not revoked and not expired
- Issues new access token + new refresh token
- Revokes old refresh token (rotation)

### Admin Login (2-step)
- Step 1: POST /api/v1/auth/admin/login → validate credentials → return partial token with `adminStep1` claim
- Step 2: POST /api/v1/auth/admin/totp-verify → validate TOTP code with OtpNet → issue full admin JWT (10 min expiry)
- Admin access token does NOT have refresh token — session ends when token expires or app closes

---

## DRIVER LIVENESS VERIFICATION

### How it works
Every driver login and registration must pass liveness before full access is granted.

### Flutter side
1. After credentials are validated, open liveness screen
2. Load face-api.js models (TinyFaceDetector + FaceLandmark68TinyNet + FaceExpressionNet) in a Flutter WebView
3. Open front camera feed in the WebView
4. Generate a random challenge from: `["blink", "turn_head_left", "turn_head_right", "smile"]`
5. Display challenge instruction in large text with 10-second countdown
6. face-api.js detects face landmarks and expressions per frame
7. Challenge logic:
   - blink: detect `leftEyeOpenProbability < 0.2 && rightEyeOpenProbability < 0.2` for 2 consecutive frames
   - turn_head_left: detect `yaw < -15 degrees` from face pose estimation
   - turn_head_right: detect `yaw > 15 degrees`
   - smile: detect `happy expression > 0.7` probability
8. On challenge pass: JavaScript sends `"LIVENESS_PASS"` to Flutter via JavascriptChannel
9. Flutter captures selfie via camera plugin (not from WebView)
10. Selfie uploaded to POST /api/v1/driver/liveness with `{ livenessResult: "pass", selfieBase64: "..." }`
11. On pass: full JWT issued, selfie stored on server
12. On fail: `"LIVENESS_FAIL"` sent to Flutter → show error, increment fail count
13. After 5 consecutive failures: POST /api/v1/driver/liveness/report-failure → backend flags account, notifies admin, returns `{ bannedUntil: "..." }`

### Backend liveness endpoint
- POST /api/v1/driver/liveness
- Validates `livenessResult == "pass"`
- Saves selfie image to storage path
- Sets `DriverProfile.LivenessVerified = true`
- Resets `LivenessFailCount = 0`
- Issues full access token (replaces LivenessPending token)

---

## RIDE POSTING & SEAT CAPACITY LOGIC

### Posting a Ride
Driver calls POST /api/v1/driver/rides with:
```json
{
  "stops": [
    { "name": "Gulshan Chowrangi", "lat": 24.9215, "lng": 67.0944, "sequenceOrder": 1 },
    { "name": "Johar Mor", "lat": 24.9108, "lng": 67.1012, "sequenceOrder": 2 },
    { "name": "Tariq Road", "lat": 24.8786, "lng": 67.0600, "sequenceOrder": 3 }
  ],
  "fare": 150,
  "maxSeats": 3
}
```

Validations:
- Driver has no active ride (RideStatus.Posted or RideStatus.Active)
- Driver has an approved car
- Driver has completed liveness
- Minimum 2 stops
- MaxSeats between 1 and driver's car AvailableSeats (max 6)
- SequenceOrder must be unique and sequential

On success: create Ride + RideStops, set `CancelGraceExpiresAt = now + 5 minutes`

### Seat Capacity Check (CRITICAL LOGIC)

When a passenger requests to book with pickupStopId and dropoffStopId:

1. Get pickup stop SequenceOrder (e.g., 1) and dropoff stop SequenceOrder (e.g., 3)
2. Find all Accepted/Active bookings on the same ride
3. For each existing booking, determine if it overlaps with the requested segment:
   - Existing booking overlaps if: `existingPickup.SequenceOrder < requestedDropoff.SequenceOrder AND existingDropoff.SequenceOrder > requestedPickup.SequenceOrder`
4. Count overlapping bookings
5. If `overlappingCount >= ride.MaxSeats`: reject with error `STOP_SEGMENT_FULL`
6. If `overlappingCount < ride.MaxSeats`: allow booking request

Implement this as a Dapper query in `SeatCapacityQuery.cs` for performance:
```sql
SELECT COUNT(*) FROM "Bookings" b
JOIN "RideStops" ps ON b."PickupStopId" = ps."Id"
JOIN "RideStops" ds ON b."DropoffStopId" = ds."Id"
WHERE b."RideId" = @rideId
  AND b."Status" IN ('Accepted', 'Active')
  AND ps."SequenceOrder" < @requestedDropoffOrder
  AND ds."SequenceOrder" > @requestedPickupOrder
```

---

## BOOKING FLOW (COMPLETE)

### Step 1 — Passenger sends booking request
- Validate: passenger has no active booking, pickup stop not passed, segment not full
- Create Booking with Status = Pending, RequestedAt = now
- Send SignalR event `BookingRequestReceived` to driver's user group
- Start 2-minute timeout via background job

### Step 2 — Driver accepts or rejects (2-minute window)
**Accept:**
- Re-validate seat capacity (race condition check)
- Set Booking.Status = Accepted, AcceptedAt = now
- Send SignalR `BookingAccepted` to passenger
- Send SignalR `BookingRequestReceived` update to driver showing updated passenger list

**Reject:**
- Set Booking.Status = Rejected
- Send SignalR `BookingRejected` to passenger

**Timeout (background job, runs every 30 seconds):**
- Find all Pending bookings where `RequestedAt < now - 2 minutes`
- Set Status = Cancelled
- Send SignalR `BookingAutoCancelled` to passenger

### Step 3 — Passenger cancels (after acceptance)
- Only allowed while `Booking.Status == Accepted` and pickup stop not yet completed
- Require `cancelReason` in request body (non-empty, min 10 chars)
- Classify reason via `TrustService.ClassifyCancellation(reason)`:
  - Frivolous keywords list: `["don't want", "changed mind", "just cancel", "bored", "nvm", "never mind", "test"]`
  - If reason contains any frivolous keyword or is under 20 chars with no substance: Frivolous
  - Otherwise: Valid
- Set Booking.Status = Cancelled
- Log in CancellationLog
- If Frivolous: call `TrustService.DeductPoints(userId, 5)`
- Check thresholds:
  - Frivolous cancellations >= 3: send warning notification
  - Frivolous cancellations >= 5: suspend account, send suspension notification + email

### Step 4 — Driver cancels a ride
- If within grace period (now < CancelGraceExpiresAt): cancel with no penalty regardless of reason
- If after grace period: same classification logic, same penalty system
- Set Ride.Status = Cancelled
- Notify all Accepted passengers via SignalR `RideCancelledByDriver` + email

### Step 5 — No-show
- Passenger no-show: driver waits 3 minutes at pickup stop, then clicks "No Show" per passenger
  - POST /api/v1/driver/bookings/{bookingId}/no-show
  - Log in NoShowLog (offender = passenger)
  - Deduct 10 trust points from passenger
  - Set booking status = Cancelled
- Driver no-show: passenger waits 3 minutes, clicks "No Show"
  - POST /api/v1/passenger/bookings/{bookingId}/no-show
  - Log in NoShowLog (offender = driver)
  - Deduct 10 trust points from driver

---

## STOP PROGRESSION & NAVIGATION

### Driver marks stop as arrived
PUT /api/v1/driver/stops/{stopId}/arrive

Actions:
1. Validate stop belongs to driver's active ride and is next in sequence
2. Set `RideStop.IsCompleted = true`, `ArrivedAt = now`
3. Find all passengers whose PickupStopId = this stop → send `DriverArrivedAtStop` SignalR event to each
4. Find all future bookings for passengers who wanted to select this stop → those stops are now greyed out on passenger side (frontend reads IsCompleted from ride detail API)
5. Generate new navigation deep-link to next stop:
   - Get driver's current GPS from DriverLocations table
   - Get next stop's Lat/Lng
   - Build URL: `https://maps.google.com/?saddr={currentLat},{currentLng}&daddr={nextStopLat},{nextStopLng}`
6. Send `NavigationLinkUpdated` SignalR event to driver with new link and next stop name

### End Ride per passenger
PUT /api/v1/driver/bookings/{bookingId}/end-ride

1. Set Booking.Status = Completed, CompletedAt = now
2. Send `RideEndedForPassenger` SignalR event to that passenger with fare amount

### Cash received
PUT /api/v1/driver/bookings/{bookingId}/cash-received

1. Set Booking.IsCashPaid = true
2. Send `CashReceivedConfirmed` SignalR event to that passenger
3. Both driver and passenger are now in ratings flow for this booking

### Ride fully complete
When all bookings for a ride are Completed or Cancelled: set Ride.Status = Completed, CompletedAt = now

---

## SIGNALR HUB

File: `PakiCarpool.API/Hubs/PakiCarpoolHub.cs`

```csharp
[Authorize]
public class PakiCarpoolHub : Hub
{
    // Client joins their personal group on connect
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        await base.OnConnectedAsync();
    }

    // Driver joins ride group when ride starts
    public async Task JoinRideGroup(string rideId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ride-{rideId}");
    }

    // Passenger joins ride group on booking acceptance
    public async Task JoinPassengerRideGroup(string rideId, string passengerId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ride-{rideId}-pax-{passengerId}");
    }

    // Driver sends location update every 5 seconds
    public async Task UpdateLocation(string rideId, double lat, double lng)
    {
        // 1. Upsert to DriverLocations table
        // 2. Broadcast to all in ride-{rideId} group
        await Clients.Group($"ride-{rideId}").SendAsync("ReceiveLocationUpdate", new { lat, lng, timestamp = DateTime.UtcNow });
    }

    // Chat — strictly scoped to one booking (one passenger ↔ their driver)
    public async Task SendChatMessage(string bookingId, string receiverId, string message)
    {
        // 1. Validate sender is participant in this booking
        // 2. Validate booking status is Active
        // 3. Persist to ChatMessages with ExpiresAt = now + 7 days
        // 4. Deliver ONLY to receiver's personal group
        await Clients.Group($"user-{receiverId}").SendAsync("ReceiveChatMessage", new
        {
            bookingId,
            senderId = Context.UserIdentifier,
            message,
            sentAt = DateTime.UtcNow
        });
    }

    public async Task LeaveRideGroup(string rideId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ride-{rideId}");
    }
}
```

All server-to-client broadcasts (booking accepted, driver arrived, etc.) are sent from Services layer using `IHubContext<PakiCarpoolHub>`.

---

## ALL API ENDPOINTS

### Base URL: `/api/v1`
### API versioning: URI-based from day one

#### Auth
| Method | Route | Auth | Description |
|---|---|---|---|
| POST | /auth/register | None | Register passenger or driver |
| POST | /auth/verify-email | None | Verify email OTP |
| POST | /auth/login | None | Email/password login |
| POST | /auth/refresh | Cookie | Rotate refresh token |
| POST | /auth/logout | Bearer | Revoke refresh token |
| POST | /auth/forgot-password | None | Send reset email |
| POST | /auth/reset-password | None | Reset with token |
| GET | /auth/google | None | Start Google OAuth |
| GET | /auth/google/callback | None | OAuth callback |
| POST | /auth/admin/login | None | Admin step 1 |
| POST | /auth/admin/totp-verify | Partial | Admin step 2 (TOTP) |

#### Driver
| Method | Route | Auth | Description |
|---|---|---|---|
| POST | /driver/liveness | Bearer(Driver) | Submit liveness result + selfie |
| POST | /driver/liveness/report-failure | Bearer(Driver) | Report consecutive failure |
| POST | /driver/car | Bearer(Driver) | Register car |
| PUT | /driver/car/{carId} | Bearer(Driver) | Update car details |
| GET | /driver/car/status | Bearer(Driver) | Get car approval status |
| POST | /driver/rides | Bearer(Driver) | Post new ride |
| DELETE | /driver/rides/{rideId} | Bearer(Driver) | Cancel ride |
| GET | /driver/rides/active | Bearer(Driver) | Get active ride |
| GET | /driver/rides/{rideId}/bookings | Bearer(Driver) | List all bookings on a ride |
| PUT | /driver/bookings/{bookingId}/accept | Bearer(Driver) | Accept booking |
| PUT | /driver/bookings/{bookingId}/reject | Bearer(Driver) | Reject booking |
| PUT | /driver/bookings/{bookingId}/no-show | Bearer(Driver) | Mark passenger no-show |
| PUT | /driver/bookings/{bookingId}/end-ride | Bearer(Driver) | End ride for passenger |
| PUT | /driver/bookings/{bookingId}/cash-received | Bearer(Driver) | Confirm cash |
| PUT | /driver/stops/{stopId}/arrive | Bearer(Driver) | Mark stop arrived |
| POST | /driver/location | Bearer(Driver) | Update GPS location |
| POST | /driver/ratings | Bearer(Driver) | Rate passenger |
| POST | /driver/flags | Bearer(Driver) | Flag a passenger |

#### Passenger
| Method | Route | Auth | Description |
|---|---|---|---|
| GET | /passenger/rides | Bearer(Passenger) | List available rides |
| GET | /passenger/rides/{rideId} | Bearer(Passenger) | Ride detail |
| POST | /passenger/bookings | Bearer(Passenger) | Create booking request |
| DELETE | /passenger/bookings/{bookingId} | Bearer(Passenger) | Cancel booking with reason |
| GET | /passenger/bookings/active | Bearer(Passenger) | Get active booking |
| PUT | /passenger/bookings/{bookingId}/no-show | Bearer(Passenger) | Mark driver no-show |
| POST | /passenger/ratings | Bearer(Passenger) | Rate driver |
| POST | /passenger/flags | Bearer(Passenger) | Flag a driver |

#### Chat
| Method | Route | Auth | Description |
|---|---|---|---|
| GET | /chat/{bookingId}/messages | Bearer | Get chat history (7-day window, scoped to booking participants only) |

#### Admin
| Method | Route | Auth | Description |
|---|---|---|---|
| GET | /admin/dashboard | Bearer(Admin) | Summary metrics |
| GET | /admin/users | Bearer(Admin) | List all users |
| GET | /admin/users/{userId} | Bearer(Admin) | User full profile |
| PUT | /admin/users/{userId}/block | Bearer(Admin) | Block account |
| PUT | /admin/users/{userId}/unblock | Bearer(Admin) | Unblock account |
| GET | /admin/driver-approvals | Bearer(Admin) | Pending car registrations |
| PUT | /admin/driver-approvals/{carId}/approve | Bearer(Admin) | Approve car |
| PUT | /admin/driver-approvals/{carId}/reject | Bearer(Admin) | Reject car |
| PUT | /admin/drivers/{driverId}/manual-verify | Bearer(Admin) | Manual liveness approval |
| GET | /admin/rides | Bearer(Admin) | All rides |
| DELETE | /admin/rides/{rideId} | Bearer(Admin) | Cancel any ride |
| GET | /admin/flags | Bearer(Admin) | View flags |
| PUT | /admin/flags/{flagId}/dismiss | Bearer(Admin) | Dismiss flag |
| PUT | /admin/flags/{flagId}/action | Bearer(Admin) | Act on flag |
| GET | /admin/reports | Bearer(Admin) | Reports |
| GET | /admin/reports/export | Bearer(Admin) | Export CSV/PDF |

### Standard Error Response Format
```json
{
  "statusCode": 400,
  "error": "ERROR_CODE",
  "message": "Human-readable description"
}
```

### Error Codes
`VALIDATION_ERROR`, `STOP_ALREADY_PASSED`, `STOP_SEGMENT_FULL`, `ACTIVE_BOOKING_EXISTS`, `ACTIVE_RIDE_EXISTS`, `CAR_NOT_APPROVED`, `LIVENESS_REQUIRED`, `LIVENESS_FAILED`, `LIVENESS_BANNED`, `UNAUTHORIZED`, `TOKEN_EXPIRED`, `FORBIDDEN`, `NOT_FOUND`, `BOOKING_TIMEOUT`, `ACCOUNT_LOCKED`, `ACCOUNT_SUSPENDED`, `RATE_LIMIT_EXCEEDED`, `INTERNAL_ERROR`

---

## SECURITY IMPLEMENTATION

### Program.cs — Middleware Pipeline Order (ORDER MATTERS)
```csharp
app.UseHttpsRedirection();
app.UseHsts();
app.UseCors("PakiCarpoolPolicy");
app.UseRateLimiting();           // custom middleware
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<PakiCarpoolHub>("/hubs/pakicarpool");
```

### CORS Policy
```csharp
builder.Services.AddCors(options => {
    options.AddPolicy("PakiCarpoolPolicy", policy => {
        policy.WithOrigins("https://app.pakicarpool.pk", "https://admin.pakicarpool.pk")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();  // Required for httpOnly cookie
    });
});
```

### Rate Limiting
- General: 100 requests/minute per IP
- Auth endpoints: 10 requests/minute per IP
- Implement using `AspNetCoreRateLimit` package

### HSTS
```csharp
app.UseHsts();
builder.Services.AddHsts(options => {
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});
```

### BCrypt
```csharp
// Hash: BCryptHelper.HashPassword(plainText) → work factor 12
string hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
// Verify: BCryptHelper.VerifyPassword(plainText, hash)
bool valid = BCrypt.Net.BCrypt.Verify(password, hash);
```

### AES-256 Encryption (for sensitive fields)
Implement `AesEncryptionHelper` with:
- `Encrypt(string plaintext, string key)` → Base64 ciphertext
- `Decrypt(string ciphertext, string key)` → plaintext
- Key from environment variable `AES_ENCRYPTION_KEY`
- Use for: `DriverCar.LicensePlate`, `TotpSecret.Secret`

### TOTP (Admin 2FA)
```csharp
// OtpNet package
var secret = KeyGeneration.GenerateRandomKey(20);
var totp = new Totp(secret);
bool valid = totp.VerifyTotp(userInput, out long timeStep, new VerificationWindow(1, 1));
```

---

## BACKGROUND JOBS

Implement all as `BackgroundService` (`IHostedService`) registered in `Program.cs`:

### BookingTimeoutJob — runs every 30 seconds
```
Find all Bookings where Status = Pending AND RequestedAt < NOW() - 2 minutes
For each: set Status = Cancelled, send SignalR BookingAutoCancelled to passenger
```

### ChatExpiryJob — runs every hour
```
DELETE FROM ChatMessages WHERE ExpiresAt < NOW()
```

### RefreshTokenCleanupJob — runs daily at 02:00
```
DELETE FROM RefreshTokens WHERE IsRevoked = true OR ExpiresAt < NOW()
```

### TrustScoreJob — runs daily at 03:00
```
For all drivers: recalculate AverageRating from Ratings table, update DriverProfile
For all passengers: recalculate AverageRating
```

### RideGraceExpiryJob — runs every 30 seconds
```
Find rides where Status = Posted AND CancelGraceExpiresAt < NOW()
Mark these rides' grace period as expired (set a flag)
(No auto-action — just used for penalty enforcement on late cancellations)
```

---

## TRUST SCORE SYSTEM

Implement in `TrustService.cs`:

```
Initial score: 100
Frivolous cancellation: -5 points
No-show: -10 points
Positive rating (4-5 stars): +3 points
Ride completion: +1 point

Thresholds:
- Frivolous cancellations = 3 → send in-app + email warning
- Frivolous cancellations = 5 → suspend account, in-app + phone + email notification
- No-shows = 2 → send in-app + email warning
- No-shows = 4 → suspend account
- Trust score < 50 → warning
- Trust score < 30 → suspend
```

Admin can manually unblock any suspended account.

---

## NOTIFICATION SYSTEM

Implement `NotificationService.cs` with three channels:

**In-App (SignalR):** Send to user's personal group `user-{userId}`

**Email (Brevo SMTP via MailKit):**
```csharp
var message = new MimeMessage();
message.From.Add(new MailboxAddress("Paki Carpool", "noreply@pakicarpool.pk"));
// Configure SmtpClient with Brevo SMTP credentials from environment variables
```

**Phone notification:** Implement as in-app push simulation via SignalR for now (native push via FCM is out of scope v1 — but structure the NotificationService so FCM can be plugged in later without changing callers)

### Notification Matrix
| Event | In-App | Email |
|---|---|---|
| Email OTP | No | Yes |
| Car approved/rejected | Yes | Yes |
| Booking request (driver) | Yes | No |
| Booking accepted/rejected | Yes | No |
| Booking auto-cancelled | Yes | No |
| Driver arrived at stop | Yes | No |
| Ride ended | Yes | No |
| Ride cancelled by driver | Yes | Yes |
| No-show recorded | Yes | Yes |
| Warning issued | Yes | Yes |
| Account suspended | Yes | Yes |
| Password reset | No | Yes |
| New driver registration (admin) | Admin portal | No |
| Liveness failure (admin) | Admin portal | No |

---

## FLUTTER APP STRUCTURE

### Passenger/Driver App

```
lib/
  main.dart
  /core
    /constants
    /theme
    /utils
    /network
      api_client.dart          (Dio HTTP client with interceptors)
      signalr_client.dart      (SignalR connection manager)
  /features
    /auth
      /screens
        role_selection_screen.dart
        register_screen.dart
        login_screen.dart
        otp_verify_screen.dart
        forgot_password_screen.dart
      /providers
        auth_provider.dart
      /services
        auth_service.dart
    /liveness
      /screens
        liveness_screen.dart   (WebView + face-api.js)
      /services
        liveness_service.dart
    /driver
      /screens
        car_registration_screen.dart
        post_ride_screen.dart
        active_ride_dashboard.dart
        booking_requests_screen.dart
      /providers
        driver_provider.dart
      /services
        driver_service.dart
    /passenger
      /screens
        ride_listing_screen.dart
        ride_detail_screen.dart
        active_ride_screen.dart (with flutter_map)
        payment_screen.dart
      /providers
        passenger_provider.dart
      /services
        passenger_service.dart
    /chat
      /screens
        chat_screen.dart
      /providers
        chat_provider.dart
    /ratings
      /screens
        rating_screen.dart
    /profile
      /screens
        profile_screen.dart
  /shared
    /widgets
    /models
```

### Admin App (separate Flutter project)

```
lib/
  main.dart
  /core
  /features
    /auth
      admin_login_screen.dart
      totp_screen.dart
    /dashboard
      dashboard_screen.dart
    /users
      user_list_screen.dart
      user_detail_screen.dart
    /approvals
      driver_approval_screen.dart
    /rides
      ride_list_screen.dart
      ride_detail_screen.dart
    /flags
      flags_screen.dart
    /reports
      reports_screen.dart
```

---

## KEY FLUTTER IMPLEMENTATION DETAILS

### Map (Passenger active ride screen)
```dart
// flutter_map with OpenStreetMap tiles
FlutterMap(
  mapController: _mapController,
  options: MapOptions(center: driverLocation, zoom: 14),
  children: [
    TileLayer(
      urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      userAgentPackageName: 'pk.pakicarpool.app',
    ),
    MarkerLayer(markers: [
      Marker(point: driverLocation, child: Icon(Icons.directions_car, color: Colors.blue)),
      Marker(point: passengerPickupStop, child: Icon(Icons.flag, color: Colors.red)),
    ]),
  ],
)
```

SignalR updates driverLocation LatLng every 5 seconds → `_mapController.move(newLocation, 14)` to follow driver.

### Navigation deep-link (Driver)
```dart
final mapsUrl = 'https://maps.google.com/?saddr=$currentLat,$currentLng&daddr=$stopLat,$stopLng';
if (await canLaunchUrl(Uri.parse(mapsUrl))) {
  await launchUrl(Uri.parse(mapsUrl), mode: LaunchMode.externalApplication);
}
```

### SignalR Connection
```dart
// signalr_netcore package
final hubConnection = HubConnectionBuilder()
  .withUrl('https://api.pakicarpool.pk/hubs/pakicarpool',
      options: HttpConnectionOptions(
        accessTokenFactory: () async => await authProvider.getAccessToken(),
      ))
  .withAutomaticReconnect()
  .build();

await hubConnection.start();

// Listen for location updates
hubConnection.on('ReceiveLocationUpdate', (args) {
  final data = args![0] as Map<String, dynamic>;
  locationProvider.update(data['lat'], data['lng']);
});
```

### JWT Storage (Flutter)
- Access token: `flutter_secure_storage` (encrypted on device)
- Refresh token: httpOnly cookie managed by the browser/http client automatically
- Never store tokens in SharedPreferences or regular storage

---

## NGINX CONFIGURATION

```nginx
server {
    listen 80;
    server_name api.pakicarpool.pk;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name api.pakicarpool.pk;

    ssl_certificate /etc/letsencrypt/live/api.pakicarpool.pk/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.pakicarpool.pk/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers on;

    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options nosniff;
    add_header X-Frame-Options DENY;
    add_header X-XSS-Protection "1; mode=block";

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";   # Required for WebSocket/SignalR
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_cache_bypass $http_upgrade;
    }
}
```

Repeat for `admin.pakicarpool.pk` pointing to a different port or path.

---

## ENVIRONMENT VARIABLES (never hardcode)

```env
DATABASE_URL=Host=localhost;Port=5432;Database=pakicarpool;Username=...;Password=...
JWT_SECRET=<256-bit random string>
JWT_ISSUER=api.pakicarpool.pk
JWT_AUDIENCE=pakicarpool-clients
AES_ENCRYPTION_KEY=<256-bit random string>
GOOGLE_CLIENT_ID=<from Google Cloud Console>
GOOGLE_CLIENT_SECRET=<from Google Cloud Console>
BREVO_SMTP_HOST=smtp-relay.brevo.com
BREVO_SMTP_PORT=587
BREVO_SMTP_USER=<brevo account email>
BREVO_SMTP_PASS=<brevo SMTP key>
FROM_EMAIL=noreply@pakicarpool.pk
FILE_STORAGE_PATH=/var/pakicarpool/uploads
ADMIN_DOMAIN=admin.pakicarpool.pk
APP_DOMAIN=app.pakicarpool.pk
```

---

## CODING STANDARDS

1. Every controller method must be thin — no business logic, only call service method and return result
2. Every service method must go through its interface — no direct concrete class injection
3. All database operations go through repository layer — no DbContext calls in services
4. Dapper used only for: seat capacity queries, analytics, reports, live location aggregations
5. EF Core used for: all CRUD, migrations, relationship loading
6. All async operations use `async/await` — no `.Result` or `.Wait()`
7. All endpoints return consistent response format: `{ data: ..., message: ... }` or `{ statusCode, error, message }`
8. Never return stack traces to client — `ExceptionHandlingMiddleware` catches all unhandled exceptions
9. Input validation using Data Annotations + FluentValidation on all DTOs
10. No hardcoded strings — use `PakiCarpool.Common/Constants/`
11. All sensitive config in environment variables, read via `IConfiguration`
12. Every public method in Services layer must have XML doc comments
13. Repository methods must be unit-testable (use interfaces, avoid statics)

---

## BUILD ORDER

Build in this exact order to avoid dependency issues:

1. `PakiCarpool.Domain` — entities, DTOs, enums
2. `PakiCarpool.Common` — utilities
3. `PakiCarpool.Interfaces` — service + repository interfaces
4. `PakiCarpool.Security` — JWT, BCrypt, AES, TOTP helpers
5. `PakiCarpool.Repository` — DbContext, EF configs, migrations, Dapper queries
6. `PakiCarpool.Services` — business logic implementations + background jobs
7. `PakiCarpool.API` — controllers, hub, middleware, Program.cs, DI wiring
8. Flutter Passenger/Driver App
9. Flutter Admin App

---

## WHAT TO BUILD FIRST (Recommended Sequence)

1. Domain models + enums
2. Database schema (EF migrations) — get the DB running
3. Auth system end-to-end (register, verify, login, JWT, refresh, logout)
4. Driver car registration + admin approval flow
5. Liveness detection (Flutter WebView + backend endpoint)
6. Ride posting + seat capacity logic
7. Booking flow (request → accept/reject → timeout)
8. SignalR hub + location broadcasting
9. Stop progression + navigation links
10. Per-passenger ride management (arrived, end ride, cash received)
11. Chat (scoped to booking, 7-day expiry)
12. Trust score + cancellation classification
13. No-show system
14. Ratings
15. Admin portal (all features)
16. Notifications (in-app + email)
17. Background jobs
18. Security hardening (rate limiting, HSTS, CORS, input sanitization)
19. Flutter UI polish
20. Nginx + SSL setup

---

## IMPORTANT REMINDERS

- Chat is STRICTLY between a booked passenger and the driver of that specific booking. One passenger cannot see another passenger's chat. Driver has separate chat threads per passenger. Chat is only available while `Booking.Status == Active`. After ride ends, chat is read-only for 7 days then deleted.
- The Google Maps navigation link does NOT require an API key. It is just a URL that opens the native Maps app.
- Liveness check happens at EVERY driver login, not just registration.
- A driver CANNOT post a new ride if any ride with Status = Posted or Active exists for them.
- A passenger CANNOT book more than one ride at a time.
- Admin accounts are SEEDED in the database — there is no public admin registration endpoint.
- Seat capacity is checked PER SEGMENT, not per ride total. A ride with 3 max seats can have different passenger counts on different stop segments.
- The cancellation grace period for drivers is 5 minutes from ride posting. After that, frivolous cancellations incur penalties.
- All secrets must come from environment variables. The `appsettings.json` should only contain non-sensitive configuration and placeholder references.
