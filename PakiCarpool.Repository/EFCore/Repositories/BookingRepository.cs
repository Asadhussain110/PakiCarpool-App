using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PakiCarpool.Common.Constants;
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
    public Task<(BookingAcceptanceOutcome Outcome, Booking? Booking)> AcceptPendingAsync(
        Guid bookingId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        return _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var rideId = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Id == bookingId)
                .Select(b => (Guid?)b.RideId)
                .FirstOrDefaultAsync(cancellationToken);
            if (rideId is null)
                return (BookingAcceptanceOutcome.NotFound, (Booking?)null);

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var ride = await GetRideForUpdateAsync(rideId.Value, cancellationToken);
            if (ride is null)
                return (BookingAcceptanceOutcome.RideNotFound, (Booking?)null);

            // AsNoTracking is required here: the service may already have tracked a stale
            // Pending booking while this operation waited for another acceptance or timeout.
            var booking = await GetFreshBookingAsync(bookingId, cancellationToken);
            if (booking is null)
                return (BookingAcceptanceOutcome.NotFound, (Booking?)null);
            booking.Ride = ride;

            if (ride.Status == RideStatus.Cancelled)
                return (BookingAcceptanceOutcome.RideCancelled, booking);
            if (ride.Status == RideStatus.Completed)
                return (BookingAcceptanceOutcome.RideCompleted, booking);

            var nonPendingOutcome = booking.Status switch
            {
                BookingStatus.Pending => (BookingAcceptanceOutcome?)null,
                BookingStatus.Accepted => BookingAcceptanceOutcome.AlreadyAccepted,
                BookingStatus.Cancelled => BookingAcceptanceOutcome.Cancelled,
                BookingStatus.Rejected => BookingAcceptanceOutcome.Rejected,
                _ => BookingAcceptanceOutcome.NotPending
            };
            if (nonPendingOutcome is not null)
                return (nonPendingOutcome.Value, booking);

            if (booking.PickupStop is null || booking.DropoffStop is null)
                return (BookingAcceptanceOutcome.StopsNotFound, booking);

            var cutoff = utcNow.AddMinutes(-AppConstants.BookingTimeoutMinutes);
            if (booking.RequestedAt < cutoff)
            {
                var cancelled = await CancelTimedOutUnderRideLockAsync(
                    booking,
                    cutoff,
                    cancellationToken);
                if (!cancelled)
                    return (BookingAcceptanceOutcome.NotPending, (Booking?)null);

                await transaction.CommitAsync(cancellationToken);
                booking.Status = BookingStatus.Cancelled;
                booking.CancelReason = AppConstants.BookingTimeoutCancelReason;
                SynchronizeTrackedBooking(booking);
                return (BookingAcceptanceOutcome.TimedOut, booking);
            }

            var overlapping = await _seatCapacityQuery.GetOverlappingBookingCountAsync(
                booking.RideId,
                booking.PickupStop.SequenceOrder,
                booking.DropoffStop.SequenceOrder,
                _context.Database.GetDbConnection(),
                transaction.GetDbTransaction(),
                cancellationToken);
            if (overlapping >= ride.MaxSeats)
                return (BookingAcceptanceOutcome.SegmentFull, booking);

            var updated = await _context.Bookings
                .Where(b => b.Id == bookingId && b.Status == BookingStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(b => b.Status, BookingStatus.Accepted)
                        .SetProperty(b => b.AcceptedAt, utcNow),
                    cancellationToken);
            if (updated != 1)
                return (BookingAcceptanceOutcome.NotPending, (Booking?)null);

            await transaction.CommitAsync(cancellationToken);
            booking.Status = BookingStatus.Accepted;
            booking.AcceptedAt = utcNow;
            SynchronizeTrackedBooking(booking);
            return (BookingAcceptanceOutcome.Accepted, booking);
        });
    }

    /// <inheritdoc />
    public Task<Booking?> CancelTimedOutAsync(
        Guid bookingId,
        DateTime cutoff,
        CancellationToken cancellationToken = default)
    {
        return _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var rideId = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Id == bookingId)
                .Select(b => (Guid?)b.RideId)
                .FirstOrDefaultAsync(cancellationToken);
            if (rideId is null)
                return null;

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var ride = await GetRideForUpdateAsync(rideId.Value, cancellationToken);
            if (ride is null)
                return null;

            var booking = await GetFreshBookingAsync(bookingId, cancellationToken);
            if (booking is null ||
                booking.Status != BookingStatus.Pending ||
                booking.RequestedAt >= cutoff)
                return null;

            booking.Ride = ride;
            var cancelled = await CancelTimedOutUnderRideLockAsync(
                booking,
                cutoff,
                cancellationToken);
            if (!cancelled)
                return null;

            await transaction.CommitAsync(cancellationToken);
            booking.Status = BookingStatus.Cancelled;
            booking.CancelReason = AppConstants.BookingTimeoutCancelReason;
            SynchronizeTrackedBooking(booking);
            return booking;
        });
    }

    private Task<Ride?> GetRideForUpdateAsync(Guid rideId, CancellationToken cancellationToken)
    {
        return _context.Rides
            .FromSqlInterpolated($"SELECT * FROM \"Rides\" WHERE \"Id\" = {rideId} FOR UPDATE")
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<Booking?> GetFreshBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        return _context.Bookings
            .AsNoTracking()
            .Include(b => b.Passenger)
            .Include(b => b.PickupStop)
            .Include(b => b.DropoffStop)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
    }

    private async Task<bool> CancelTimedOutUnderRideLockAsync(
        Booking booking,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var updated = await _context.Bookings
            .Where(b => b.Id == booking.Id &&
                b.Status == BookingStatus.Pending &&
                b.RequestedAt < cutoff)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.Status, BookingStatus.Cancelled)
                    .SetProperty(b => b.CancelReason, AppConstants.BookingTimeoutCancelReason),
                cancellationToken);
        return updated == 1;
    }

    private void SynchronizeTrackedBooking(Booking booking)
    {
        var tracked = _context.Bookings.Local.FirstOrDefault(b => b.Id == booking.Id);
        if (tracked is null)
            return;

        tracked.Status = booking.Status;
        tracked.AcceptedAt = booking.AcceptedAt;
        tracked.CancelReason = booking.CancelReason;
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
                b.Status != BookingStatus.Cancelled &&
                b.Status != BookingStatus.Rejected);
    }
}
