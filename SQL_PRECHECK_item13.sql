-- Item 13 schema-integrity precheck.
-- Read-only: every statement is a SELECT. Run the entire script and resolve any
-- returned rows before applying 20261125095018_AddSchemaIntegritySafeguards.

-- Case-insensitive duplicate emails (IX_Users_Email_Lower).
SELECT u."Id", u."Email", LOWER(u."Email") AS "NormalizedEmail"
FROM "Users" AS u
WHERE LOWER(u."Email") IN (
    SELECT LOWER(u2."Email")
    FROM "Users" AS u2
    GROUP BY LOWER(u2."Email")
    HAVING COUNT(*) > 1
)
ORDER BY LOWER(u."Email"), u."Id";

-- Rating/star, fare, and seat checks.
SELECT r."Id", r."Stars"
FROM "Ratings" AS r
WHERE r."Stars" < 1 OR r."Stars" > 5;

SELECT r."Id", r."Fare"
FROM "Rides" AS r
WHERE r."Fare" < 0;

SELECT r."Id", r."MaxSeats"
FROM "Rides" AS r
WHERE r."MaxSeats" < 1 OR r."MaxSeats" > 6;

-- Existing DriverCars seat check (retained, not replaced).
SELECT c."Id", c."AvailableSeats"
FROM "DriverCars" AS c
WHERE c."AvailableSeats" < 1 OR c."AvailableSeats" > 6;

-- Coordinate checks. NOT BETWEEN also reports NaN and infinities.
SELECT s."Id", s."Latitude"
FROM "RideStops" AS s
WHERE NOT (s."Latitude" BETWEEN -90 AND 90);

SELECT s."Id", s."Longitude"
FROM "RideStops" AS s
WHERE NOT (s."Longitude" BETWEEN -180 AND 180);

SELECT l."Id", l."Latitude"
FROM "DriverLocations" AS l
WHERE NOT (l."Latitude" BETWEEN -90 AND 90);

SELECT l."Id", l."Longitude"
FROM "DriverLocations" AS l
WHERE NOT (l."Longitude" BETWEEN -180 AND 180);

-- Orphaned DriverLocations foreign keys.
SELECT l."Id", l."DriverId"
FROM "DriverLocations" AS l
WHERE NOT EXISTS (SELECT 1 FROM "DriverProfiles" AS p WHERE p."Id" = l."DriverId");

SELECT l."Id", l."RideId"
FROM "DriverLocations" AS l
WHERE NOT EXISTS (SELECT 1 FROM "Rides" AS r WHERE r."Id" = l."RideId");

-- Orphaned Ratings foreign keys.
SELECT r."Id", r."RideId"
FROM "Ratings" AS r
WHERE NOT EXISTS (SELECT 1 FROM "Rides" AS ride WHERE ride."Id" = r."RideId");

SELECT r."Id", r."RaterId"
FROM "Ratings" AS r
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = r."RaterId");

SELECT r."Id", r."RateeId"
FROM "Ratings" AS r
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = r."RateeId");

-- Orphaned Flags foreign keys.
SELECT f."Id", f."ReporterId"
FROM "Flags" AS f
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = f."ReporterId");

SELECT f."Id", f."ReporteeId"
FROM "Flags" AS f
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = f."ReporteeId");

SELECT f."Id", f."RideId"
FROM "Flags" AS f
WHERE NOT EXISTS (SELECT 1 FROM "Rides" AS r WHERE r."Id" = f."RideId");

-- Orphaned NoShowLogs foreign keys (BookingId is optional; only non-NULL values are checked).
SELECT n."Id", n."ReporterId"
FROM "NoShowLogs" AS n
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = n."ReporterId");

SELECT n."Id", n."OffenderId"
FROM "NoShowLogs" AS n
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = n."OffenderId");

SELECT n."Id", n."RideId"
FROM "NoShowLogs" AS n
WHERE NOT EXISTS (SELECT 1 FROM "Rides" AS r WHERE r."Id" = n."RideId");

SELECT n."Id", n."BookingId"
FROM "NoShowLogs" AS n
WHERE n."BookingId" IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM "Bookings" AS b WHERE b."Id" = n."BookingId");

-- Orphaned CancellationLogs foreign key.
SELECT c."Id", c."UserId"
FROM "CancellationLogs" AS c
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = c."UserId");

-- Orphaned ChatMessages foreign keys.
SELECT m."Id", m."SenderId"
FROM "ChatMessages" AS m
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = m."SenderId");

SELECT m."Id", m."ReceiverId"
FROM "ChatMessages" AS m
WHERE NOT EXISTS (SELECT 1 FROM "Users" AS u WHERE u."Id" = m."ReceiverId");

-- Existing partial unique indexes represented in EF metadata by this migration.
SELECT b."PassengerId", ARRAY_AGG(b."Id" ORDER BY b."Id") AS "BookingIds", COUNT(*) AS "ActiveBookingCount"
FROM "Bookings" AS b
WHERE b."Status" IN ('Pending', 'Accepted', 'Active')
GROUP BY b."PassengerId"
HAVING COUNT(*) > 1;

SELECT r."DriverId", ARRAY_AGG(r."Id" ORDER BY r."Id") AS "RideIds", COUNT(*) AS "ActiveRideCount"
FROM "Rides" AS r
WHERE r."Status" IN ('Posted', 'Active')
GROUP BY r."DriverId"
HAVING COUNT(*) > 1;
