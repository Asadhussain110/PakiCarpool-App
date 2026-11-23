using Microsoft.EntityFrameworkCore;
using PakiCarpool.Domain.DTOs.Passenger;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Repository.Dapper;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Repository.EFCore.Repositories;

/// <summary>
/// EF Core implementation of IRideRepository.
/// Handles Ride, RideStop, and DriverLocation operations.
/// </summary>
public class RideRepository : IRideRepository
{
    private readonly PakiCarpoolDbContext _context;
    private readonly RideSearchQuery _rideSearchQuery;

    public RideRepository(PakiCarpoolDbContext context, RideSearchQuery rideSearchQuery)
    {
        _context = context;
        _rideSearchQuery = rideSearchQuery;
    }

    /// <inheritdoc />
    public async Task<Ride?> GetByIdAsync(
        Guid rideId,
        bool includeStops = false,
        bool includeBookings = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Ride> query = _context.Rides;

        if (includeStops)
            query = query.Include(r => r.Stops.OrderBy(s => s.SequenceOrder));

        if (includeBookings)
            query = query.Include(r => r.Bookings)
                         .ThenInclude(b => b.Passenger);

        query = query.Include(r => r.Driver).ThenInclude(dp => dp.User)
                     .Include(r => r.Car);

        if (includeStops || includeBookings)
            query = query.AsSplitQuery();

        return await query.FirstOrDefaultAsync(r => r.Id == rideId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Ride?> GetActiveRideByDriverIdAsync(Guid driverProfileId)
    {
        return await _context.Rides
            .Include(r => r.Stops.OrderBy(s => s.SequenceOrder))
            .Include(r => r.Bookings)
            .Include(r => r.Car)
            .Include(r => r.Driver).ThenInclude(dp => dp.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.DriverId == driverProfileId &&
                (r.Status == RideStatus.Posted || r.Status == RideStatus.Active));
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveRideForUserAsync(Guid userId)
    {
        return await _context.Rides
            .AnyAsync(ride => ride.Driver.UserId == userId &&
                (ride.Status == RideStatus.Posted || ride.Status == RideStatus.Active));
    }

    /// <inheritdoc />
    public async Task<Ride?> GetMostRecentRideForUserAsync(Guid userId)
    {
        return await _context.Rides
            .Where(ride => ride.Driver.UserId == userId)
            .OrderByDescending(ride => ride.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<(List<Ride> Items, int TotalCount)> GetAvailableRidesPaginatedAsync(int page, int pageSize)
    {
        var query = _context.Rides
            .Where(r => r.Status == RideStatus.Posted)
            .Include(r => r.Stops.OrderBy(s => s.SequenceOrder))
            .Include(r => r.Driver).ThenInclude(dp => dp.User)
            .Include(r => r.Car)
            .AsSplitQuery()
            .OrderByDescending(r => r.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<List<RideSearchCandidateDto>> GetCandidateRidesAsync(
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        double radiusMeters)
    {
        return await _rideSearchQuery.GetCandidateRidesAsync(
            startLat,
            startLng,
            endLat,
            endLng,
            radiusMeters);
    }

    /// <inheritdoc />
    public async Task<(List<Ride> Items, int TotalCount)> GetAllPaginatedAsync(int page, int pageSize)
    {
        var totalCount = await _context.Rides.CountAsync();
        var items = await _context.Rides
            .Include(r => r.Stops.OrderBy(s => s.SequenceOrder))
            .Include(r => r.Driver).ThenInclude(dp => dp.User)
            .Include(r => r.Car)
            .AsSplitQuery()
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<Ride> CreateAsync(Ride ride)
    {
        _context.Rides.Add(ride);
        await _context.SaveChangesAsync();
        return ride;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Ride ride)
    {
        _context.Rides.Update(ride);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<RideStop?> GetStopByIdAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        return await _context.RideStops
            .Include(rs => rs.Ride)
            .FirstOrDefaultAsync(rs => rs.Id == stopId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateStopAsync(RideStop stop)
    {
        _context.RideStops.Update(stop);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task UpsertDriverLocationAsync(DriverLocation location)
    {
        const string sql = """
            INSERT INTO "DriverLocations" ("Id", "DriverId", "RideId", "Latitude", "Longitude", "RecordedAt")
            VALUES (@Id, @DriverId, @RideId, @Lat, @Lng, @RecordedAt)
            ON CONFLICT ("DriverId", "RideId") DO UPDATE
            SET "Latitude" = EXCLUDED."Latitude",
                "Longitude" = EXCLUDED."Longitude",
                "RecordedAt" = EXCLUDED."RecordedAt";
            """;

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("Id", Guid.NewGuid()),
            new Npgsql.NpgsqlParameter("DriverId", location.DriverId),
            new Npgsql.NpgsqlParameter("RideId", location.RideId),
            new Npgsql.NpgsqlParameter("Lat", location.Latitude),
            new Npgsql.NpgsqlParameter("Lng", location.Longitude),
            new Npgsql.NpgsqlParameter("RecordedAt", location.RecordedAt));
    }

    /// <inheritdoc />
    public async Task<DriverLocation?> GetDriverLocationAsync(Guid driverProfileId, Guid rideId)
    {
        return await _context.DriverLocations
            .FirstOrDefaultAsync(dl => dl.DriverId == driverProfileId && dl.RideId == rideId);
    }

    /// <inheritdoc />
    public async Task<int> CountTotalRidesAsync()
    {
        return await _context.Rides.CountAsync();
    }

    /// <inheritdoc />
    public async Task<int> CountActiveRidesAsync()
    {
        return await _context.Rides
            .CountAsync(r => r.Status == RideStatus.Posted || r.Status == RideStatus.Active);
    }

    /// <inheritdoc />
    public async Task<List<Ride>> GetExpiredGracePeriodRidesAsync()
    {
        return await _context.Rides
            .Where(r => r.Status == RideStatus.Posted && r.CancelGraceExpiresAt < DateTime.UtcNow)
            .ToListAsync();
    }
}
