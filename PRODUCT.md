# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Stack

Flutter (Dart) for Android and iOS, with Riverpod for application state. Passenger and Driver share one app; Admin uses a separate app. The API is ASP.NET Core on .NET 10, with PostgreSQL, EF Core, and narrowly scoped Dapper queries.

## Users

- Karachi commuters who need affordable rides along shared routes.
- Car owners who want to share spare seats and offset travel costs.
- Platform administrators responsible for driver verification, safety, and disputes.

## Product Purpose

Connect passengers and drivers travelling compatible routes, making shared rides affordable while supporting verified drivers and managed passenger safety. Success means reliable ride discovery, booking, completion, trust, and safety operations.

## Positioning

PakiCarpool is a Karachi-focused carpooling service built around ordered multi-stop routes and seat sharing. Driver liveness checks, car approval, ride ratings, flags, and trust policies support its verified-ride promise.

## Operating Context

Drivers publish a route as ordered geographic stops, set a per-seat fare and seat limit, and manage bookings per passenger and route segment. Passengers select a future pickup and a later drop-off. During an active booking, the driver location and booking-scoped chat are real-time. Ride payment is cash between the parties. Admins review cars and liveness alerts and manage users, rides, flags, and reports.

## Capabilities and Constraints

- Passenger and Driver roles are selected at registration and immutable. Admin accounts are seeded; there is no public Admin registration.
- Passenger/Driver authentication supports email/password and Google OAuth 2.0 PKCE. Email OTP verification is required before first password login. Admin login requires TOTP on every login; Admin access tokens expire after 10 minutes and have no refresh token.
- Driver liveness is required during registration and every subsequent login. It uses a randomized blink/head-turn/smile challenge with a 10-second limit. Five consecutive failures trigger a 24-hour retry restriction and an Admin alert.
- A Driver needs an approved car and no existing Posted/Active ride before posting. A ride has at least two ordered stops, coordinates, per-seat fare, and a seat cap constrained by the car (maximum six). Seat capacity is enforced for each route segment.
- Booking acceptance times out after two minutes. A Passenger may have only one active booking. Chat is limited to an accepted/active booking between its Passenger and Driver, is text-only, and expires after seven days.
- Driver GPS is sent over SignalR every five seconds during a ride. Passenger maps use OpenStreetMap tiles with Flutter's native `flutter_map`; Driver navigation opens Google Maps by URL. No paid maps APIs.
- Ride completion is cash-based. Online payment, scheduled/recurring rides, native FCM/APNs push, and multi-city expansion are out of v1 scope. Required notifications use in-app SignalR and email.
- Success responses use `{ data, message }`; failures use `{ statusCode, error, message }`. Pagination uses page and pageSize. Refresh tokens rotate, are stored in an HTTP-only Secure SameSite=Strict cookie, and expire after seven days. Passenger/Driver access tokens use Flutter Secure Storage per TSD; Admin sessions end when the app closes and have no refresh token.
- **Open backend readiness item:** the current `AuthService` treats a persistent `LivenessVerified` profile flag as sufficient for later Driver logins, which does not satisfy the every-login requirement. Keep this unresolved and visible; do not silently change the requirement or backend behavior.
- **Other known backend gaps:** car photo/document upload and retrieval are required for approval but no upload route exists; the current car-update behavior does not preserve an approved car during reapproval; Admin activity/history/report fields and OTP resend/password-composition requirements need contract review against the supplied specifications.
- .NET 10 is confirmed. SDD/TSD references to .NET 8 are outdated and should be corrected when those documents are revised.
- FRD/FSD/SDD references to “Leaflet/OSM” mean the OSM map source/experience. Use native Flutter `flutter_map`; do not embed Leaflet in a WebView.
- Persistent notification history is not required by the supplied v1 requirements and is not an inbox feature.

## Brand Commitments

The product name is PakiCarpool. InDrive, Yango, Careem, Uber, and Ola are interaction references only; do not use their marks or present PakiCarpool as affiliated with them. No confirmed logo, brand font, or existing color identity was supplied.

## Evidence on Hand

The supplied FRD, FSD, PRD, SDD, TSD, and Guardrails are the product requirements. The current ASP.NET API and DTOs are implementation evidence and may conflict with those requirements. No approved customer imagery, testimonials, or performance claims were supplied; do not fabricate them.

## Product Principles

- Make shared-route stops and segment availability understandable before a booking is requested.
- Put verification, predictable status, and safety actions into the core ride flow.
- Keep Driver actions direct and usable during a trip without obscuring route progress.
- Keep payment cash-only and infrastructure choices within the documented no-cost constraint.
