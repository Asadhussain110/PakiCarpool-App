using Microsoft.EntityFrameworkCore;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Repository.Dapper;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Repository.EFCore.Repositories;

/// <summary>
/// EF Core implementation of IBookingRepository.
/// Delegates seat capacity check to SeatCapacityQuery (Dapper) to keep Dapper out of EF Core repos.
/// </summary>
public class BookingRepository : IBookingRepository
{
    private readonly PakiCarpoolDbContext _context;
    private readonly SeatCapacityQuery _seatCapacityQuery;

    public BookingRepository(PakiCarpoolDbContext context, SeatCapacityQuery seatCapacityQuery)
    {
        _context = context;
        _seatCapacityQuery = seatCapacityQuery;
    }

    /// <inheritdoc />
    public async Task<Booking?> GetByIdAsync(
        Guid bookingId,
        bool includeRide = false,
        bool includeStops = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Booking> query = _context.Bookings;

        if (includeRide)
            query = query.Include(b => b.Ride)
                         .ThenInclude(r => r.Driver)
                         .ThenInclude(dp => dp.User);

        if (includeStops)
            query = query.Include(b => b.PickupStop)
                         .Include(b => b.DropoffStop);

        query = query.Include(b => b.Passenger);

        return await query.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Booking?> GetActiveBookingByPassengerIdAsync(Guid passengerId)
    {
        return await _context.Bookings
            .Include(b => b.Ride)
                .ThenInclude(r => r.Driver)
                .ThenInclude(dp => dp.User)
            .Include(b => b.Ride)
                .ThenInclude(r => r.Car)
            .Include(b => b.PickupStop)
            .Include(b => b.DropoffStop)
            .FirstOrDefaultAsync(b => b.PassengerId == passengerId &&
                (b.Status == BookingStatus.Pending ||
                 b.Status == BookingStatus.Accepted ||
                 b.Status == BookingStatus.Active));
    }

    /// <inheritdoc />
    public async Task<List<Booking>> GetBookingsByRideIdAsync(Guid rideId, CancellationToken cancellationToken = default)
    {
        return await _context.Bookings
            .Include(b => b.Passenger)
            .Include(b => b.PickupStop)
            .Include(b => b.DropoffStop)
            .Where(b => b.RideId == rideId)
            .OrderBy(b => b.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(List<Booking> Items, int TotalCount)> GetBookingsByRideIdAsync(
        Guid rideId,
        int page,
        int pageSize)
    {
        var query = _context.Bookings
            .Include(b => b.Passenger)
            .Include(b => b.PickupStop)
            .Include(b => b.DropoffStop)
            .Where(b => b.RideId == rideId)
            .OrderBy(b => b.RequestedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<List<Booking>> GetTimedOutPendingBookingsAsync(DateTime cutoff)
    {
        return await _context.Bookings
            .Include(b => b.Passenger)
            .Where(b => b.Status == BookingStatus.Pending && b.RequestedAt < cutoff)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Booking> CreateAsync(Booking booking)
    {
        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();
        return booking;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Booking booking)
    {
        _context.Bookings.Update(booking);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<int> GetOverlappingBookingCountAsync(Guid rideId, int requestedPickupOrder, int requestedDropoffOrder)
    {
        // Delegated to Dapper query class — no Dapper code in EF Core repository
        return await _seatCapacityQuery.GetOverlappingBookingCountAsync(rideId, requestedPickupOrder, requestedDropoffOrder);
    }

    /// <inheritdoc />
    public async Task<bool> AreAllBookingsCompletedOrCancelledAsync(Guid rideId)
    {
        return !await _context.Bookings
            .AnyAsync(b => b.RideId == rideId &&
                b.Status != BookingStatus.Completed &&
                b.Status != BookingStatus.Cancelled);
    }
}
