using Microsoft.EntityFrameworkCore;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Repository.EFCore.Repositories;

/// <summary>
/// EF Core implementation of IAdminRepository.
/// Handles flag management and rating CRUD/aggregation.
/// </summary>
public class AdminRepository : IAdminRepository
{
    private readonly PakiCarpoolDbContext _context;

    public AdminRepository(PakiCarpoolDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Flag?> GetFlagByIdAsync(Guid flagId)
    {
        return await _context.Flags.FindAsync(flagId);
    }

    /// <inheritdoc />
    public async Task<(List<Flag> Items, int TotalCount)> GetFlagsPaginatedAsync(int page, int pageSize)
    {
        var totalCount = await _context.Flags.CountAsync();
        var items = await _context.Flags
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<int> CountOpenFlagsAsync()
    {
        return await _context.Flags.CountAsync(f => f.Status == FlagStatus.Open);
    }

    /// <inheritdoc />
    public async Task<List<Flag>> GetOpenFlagsByReporteeIdAsync(Guid reporteeId)
    {
        return await _context.Flags
            .Where(f => f.ReporteeId == reporteeId && f.Status == FlagStatus.Open)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Flag> CreateFlagAsync(Flag flag, CancellationToken cancellationToken = default)
    {
        _context.Flags.Add(flag);
        await _context.SaveChangesAsync(cancellationToken);
        return flag;
    }

    /// <inheritdoc />
    public async Task UpdateFlagAsync(Flag flag)
    {
        _context.Flags.Update(flag);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<Rating> CreateRatingAsync(Rating rating)
    {
        _context.Ratings.Add(rating);
        await _context.SaveChangesAsync();
        return rating;
    }

    /// <inheritdoc />
    public async Task<decimal> GetAverageRatingAsync(Guid rateeId)
    {
        var hasRatings = await _context.Ratings.AnyAsync(r => r.RateeId == rateeId);
        if (!hasRatings)
            return 0m;

        return await _context.Ratings
            .Where(r => r.RateeId == rateeId)
            .AverageAsync(r => (decimal)r.Stars);
    }

    /// <inheritdoc />
    public async Task<List<Rating>> GetRatingsForUserAsync(Guid rateeId)
    {
        return await _context.Ratings
            .Where(r => r.RateeId == rateeId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<bool> RatingExistsAsync(Guid rideId, Guid raterId, Guid rateeId)
    {
        return await _context.Ratings
            .AnyAsync(r => r.RideId == rideId && r.RaterId == raterId && r.RateeId == rateeId);
    }
}
