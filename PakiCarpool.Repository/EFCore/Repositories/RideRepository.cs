using Microsoft.EntityFrameworkCore;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Repository.EFCore.Repositories;

/// <summary>
/// EF Core implementation of IRideRepository.
/// Handles Ride, RideStop, and DriverLocation operations.
/// </summary>
public class RideRepository : IRideRepository
{
    private readonly PakiCarpoolDbContext _context;

    public RideRepository(PakiCarpoolDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Ride?> GetByIdAsync(Guid rideId, bool includeStops = false, bool includeBookings = false)
    {
        IQueryable<Ride> query = _context.Rides;

        if (includeStops)
            query = query.Include(r => r.Stops.OrderBy(s => s.SequenceOrder));

        if (includeBookings)
            query = query.Include(r => r.Bookings)
                         .ThenInclude(b => b.Passenger);

        query = query.Include(r => r.Driver).ThenInclude(dp => dp.User)
                     .Include(r => r.Car);

        return await query.FirstOrDefaultAsync(r => r.Id == rideId);
    }

    /// <inheritdoc />
    public async Task<Ride?> GetActiveRideByDriverIdAsync(Guid driverProfileId)
    {
        return await _context.Rides
            .Include(r => r.Stops.OrderBy(s => s.SequenceOrder))
            .Include(r => r.Bookings)
            .Include(r => r.Car)
            .Include(r => r.Driver).ThenInclude(dp => dp.User)
            .FirstOrDefaultAsync(r => r.DriverId == driverProfileId &&
                (r.Status == RideStatus.Posted || r.Status == RideStatus.Active));
    }

    /// <inheritdoc />
    public async Task<(List<Ride> Items, int TotalCount)> GetAvailableRidesPaginatedAsync(int page, int pageSize)
    {
        var query = _context.Rides
            .Where(r => r.Status == RideStatus.Posted)
            .Include(r => r.Stops.OrderBy(s => s.SequenceOrder))
            .Include(r => r.Driver).ThenInclude(dp => dp.User)
            .Include(r => r.Car)
            .OrderByDescending(r => r.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<(List<Ride> Items, int TotalCount)> GetAllPaginatedAsync(int page, int pageSize)
    {
        var totalCount = await _context.Rides.CountAsync();
        var items = await _context.Rides
            .Include(r => r.Stops.OrderBy(s => s.SequenceOrder))
            .Include(r => r.Driver).ThenInclude(dp => dp.User)
            .Include(r => r.Car)
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
    public async Task<RideStop?> GetStopByIdAsync(Guid stopId)
    {
        return await _context.RideStops
            .Include(rs => rs.Ride)
            .FirstOrDefaultAsync(rs => rs.Id == stopId);
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
        var existing = await _context.DriverLocations
            .FirstOrDefaultAsync(dl => dl.DriverId == location.DriverId && dl.RideId == location.RideId);

        if (existing != null)
        {
            existing.Latitude = location.Latitude;
            existing.Longitude = location.Longitude;
            existing.RecordedAt = DateTime.UtcNow;
        }
        else
        {
            _context.DriverLocations.Add(location);
        }

        await _context.SaveChangesAsync();
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
