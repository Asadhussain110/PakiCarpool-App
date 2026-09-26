using Microsoft.EntityFrameworkCore;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Repository.EFCore.Repositories;

/// <summary>
/// EF Core implementation of IUserRepository.
/// Handles User, DriverProfile, DriverCar, RefreshToken, TotpSecret, CancellationLog, and NoShowLog.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly PakiCarpoolDbContext _context;

    public UserRepository(PakiCarpoolDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _context.Users.FindAsync(id);
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    }

    /// <inheritdoc />
    public async Task<User?> GetByGoogleIdAsync(string googleId)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.GoogleId == googleId);
    }

    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _context.Users
            .AnyAsync(u => u.Email.ToLower() == email.ToLower());
    }

    /// <inheritdoc />
    public async Task<User> CreateAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<(List<User> Items, int TotalCount)> GetAllPaginatedAsync(int page, int pageSize)
    {
        var totalCount = await _context.Users.CountAsync();
        var items = await _context.Users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<int> CountByRoleAsync(UserRole role)
    {
        return await _context.Users.CountAsync(u => u.Role == role);
    }

    /// <inheritdoc />
    public async Task<int> CountSuspendedAsync()
    {
        return await _context.Users.CountAsync(u => u.IsSuspended);
    }

    /// <inheritdoc />
    public async Task<DriverProfile?> GetDriverProfileByUserIdAsync(Guid userId)
    {
        return await _context.DriverProfiles
            .Include(dp => dp.Cars)
            .Include(dp => dp.User)
            .FirstOrDefaultAsync(dp => dp.UserId == userId);
    }

    /// <inheritdoc />
    public async Task<DriverProfile?> GetDriverProfileByIdAsync(Guid driverProfileId)
    {
        return await _context.DriverProfiles
            .Include(dp => dp.Cars)
            .Include(dp => dp.User)
            .FirstOrDefaultAsync(dp => dp.Id == driverProfileId);
    }

    /// <inheritdoc />
    public async Task<DriverProfile> CreateDriverProfileAsync(DriverProfile profile)
    {
        _context.DriverProfiles.Add(profile);
        await _context.SaveChangesAsync();
        return profile;
    }

    /// <inheritdoc />
    public async Task UpdateDriverProfileAsync(DriverProfile profile)
    {
        _context.DriverProfiles.Update(profile);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<DriverCar?> GetDriverCarByIdAsync(Guid carId)
    {
        return await _context.DriverCars
            .Include(dc => dc.Driver)
                .ThenInclude(dp => dp.User)
            .FirstOrDefaultAsync(dc => dc.Id == carId);
    }

    /// <inheritdoc />
    public async Task<List<DriverCar>> GetDriverCarsAsync(Guid driverProfileId)
    {
        return await _context.DriverCars
            .Where(dc => dc.DriverId == driverProfileId)
            .OrderByDescending(dc => dc.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<DriverCar>> GetCarsByStatusAsync(CarStatus status)
    {
        return await _context.DriverCars
            .Include(dc => dc.Driver)
                .ThenInclude(dp => dp.User)
            .Where(dc => dc.Status == status)
            .OrderByDescending(dc => dc.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<DriverCar> CreateDriverCarAsync(DriverCar car)
    {
        _context.DriverCars.Add(car);
        await _context.SaveChangesAsync();
        return car;
    }

    /// <inheritdoc />
    public async Task UpdateDriverCarAsync(DriverCar car)
    {
        _context.DriverCars.Update(car);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> GetRefreshTokenAsync(string hashedToken)
    {
        return await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == hashedToken);
    }

    /// <inheritdoc />
    public async Task CreateRefreshTokenAsync(RefreshToken token)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task UpdateRefreshTokenAsync(RefreshToken token)
    {
        _context.RefreshTokens.Update(token);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task RevokeAllRefreshTokensAsync(Guid userId)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
        }

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task CleanupRefreshTokensAsync()
    {
        var expiredOrRevoked = await _context.RefreshTokens
            .Where(rt => rt.IsRevoked || rt.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        _context.RefreshTokens.RemoveRange(expiredOrRevoked);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<TotpSecret?> GetTotpSecretAsync(Guid userId)
    {
        return await _context.TotpSecrets
            .FirstOrDefaultAsync(ts => ts.UserId == userId);
    }

    /// <inheritdoc />
    public async Task CreateTotpSecretAsync(TotpSecret secret)
    {
        _context.TotpSecrets.Add(secret);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<int> CountFrivolousCancellationsAsync(Guid userId)
    {
        return await _context.CancellationLogs
            .CountAsync(cl => cl.UserId == userId &&
                         cl.Classification == CancellationClassification.Frivolous);
    }

    /// <inheritdoc />
    public async Task<int> CountNoShowsAsync(Guid userId)
    {
        return await _context.NoShowLogs
            .CountAsync(ns => ns.OffenderId == userId);
    }

    /// <inheritdoc />
    public async Task CreateCancellationLogAsync(CancellationLog log)
    {
        _context.CancellationLogs.Add(log);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task CreateNoShowLogAsync(NoShowLog log)
    {
        _context.NoShowLogs.Add(log);
        await _context.SaveChangesAsync();
    }
}
