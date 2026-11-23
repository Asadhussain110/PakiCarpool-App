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
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users.FindAsync([id], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.GoogleId == googleId, cancellationToken);
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
    public async Task<User> CreateWithDriverProfileAsync(
        User user,
        DriverProfile? driverProfile,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            _context.Users.Add(user);
            if (driverProfile is not null)
                _context.DriverProfiles.Add(driverProfile);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return user;
        });
    }

    /// <inheritdoc />
    public async Task IncrementLoginFailAsync(Guid userId)
    {
        await _context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.LoginFailCount, user => user.LoginFailCount + 1));
    }

    /// <inheritdoc />
    public async Task ResetLoginFailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.LoginFailCount, 0)
                .SetProperty(user => user.LoginBannedUntil, (DateTime?)null), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int?> GetLoginFailCountAsync(Guid userId)
    {
        return await _context.Users
            .Where(user => user.Id == userId)
            .Select(user => (int?)user.LoginFailCount)
            .SingleOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task SetLoginBanAsync(Guid userId, DateTime bannedUntil)
    {
        await _context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.LoginFailCount, 0)
                .SetProperty(user => user.LoginBannedUntil, bannedUntil));
    }

    /// <inheritdoc />
    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int?> GetAuthTokenVersionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.Role == UserRole.Admin)
            .Select(user => (int?)user.AuthTokenVersion)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryIssuePasswordResetCodeAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        DateTime expiresAt,
        TimeSpan requestWindow,
        int requestLimit,
        CancellationToken cancellationToken = default)
    {
        var windowStart = now - requestWindow;
        var updated = await _context.Users
            .Where(user =>
                user.Id == userId &&
                user.Role != UserRole.Admin &&
                user.IsActive &&
                (user.PasswordResetRequestWindowStart == null ||
                 user.PasswordResetRequestWindowStart <= windowStart ||
                 user.PasswordResetRequestCount < requestLimit))
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.PasswordResetToken, codeHash)
                .SetProperty(user => user.PasswordResetTokenExpiry, expiresAt)
                .SetProperty(user => user.PasswordResetAttempts, 0)
                .SetProperty(
                    user => user.PasswordResetRequestCount,
                    user => user.PasswordResetRequestWindowStart == null ||
                            user.PasswordResetRequestWindowStart <= windowStart
                        ? 1
                        : user.PasswordResetRequestCount + 1)
                .SetProperty(
                    user => user.PasswordResetRequestWindowStart,
                    user => user.PasswordResetRequestWindowStart == null ||
                            user.PasswordResetRequestWindowStart <= windowStart
                        ? now
                        : user.PasswordResetRequestWindowStart),
                cancellationToken);

        return updated == 1;
    }

    public async Task<bool> IncrementPasswordResetAttemptsAsync(
        Guid userId,
        DateTime now,
        int attemptLimit,
        CancellationToken cancellationToken = default)
    {
        var updated = await _context.Users
            .Where(user =>
                user.Id == userId &&
                user.Role != UserRole.Admin &&
                user.PasswordResetToken != null &&
                user.PasswordResetTokenExpiry > now &&
                user.PasswordResetAttempts < attemptLimit)
            .ExecuteUpdateAsync(update => update
                .SetProperty(
                    user => user.PasswordResetAttempts,
                    user => user.PasswordResetAttempts + 1)
                .SetProperty(
                    user => user.PasswordResetToken,
                    user => user.PasswordResetAttempts + 1 >= attemptLimit
                        ? null
                        : user.PasswordResetToken)
                .SetProperty(
                    user => user.PasswordResetTokenExpiry,
                    user => user.PasswordResetAttempts + 1 >= attemptLimit
                        ? null
                        : user.PasswordResetTokenExpiry),
                cancellationToken);

        return updated == 1;
    }

    public async Task<bool> CompletePasswordResetAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        string passwordHash,
        int attemptLimit,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var updated = await _context.Users
                .Where(user =>
                    user.Id == userId &&
                    user.Role != UserRole.Admin &&
                    user.PasswordResetToken == codeHash &&
                    user.PasswordResetTokenExpiry > now &&
                    user.PasswordResetAttempts < attemptLimit)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(user => user.PasswordHash, passwordHash)
                    .SetProperty(user => user.PasswordResetToken, (string?)null)
                    .SetProperty(user => user.PasswordResetTokenExpiry, (DateTime?)null)
                    .SetProperty(user => user.PasswordResetAttempts, 0)
                    .SetProperty(user => user.FailedLoginCount, 0)
                    .SetProperty(user => user.LockoutUntil, (DateTime?)null)
                    .SetProperty(user => user.LoginFailCount, 0)
                    .SetProperty(user => user.LoginBannedUntil, (DateTime?)null),
                    cancellationToken);

            if (updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await _context.RefreshTokens
                .Where(token => token.UserId == userId && !token.IsRevoked)
                .ExecuteUpdateAsync(update => update.SetProperty(token => token.IsRevoked, true), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<bool> TryIssueAdminPasswordResetCodeAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        DateTime expiresAt,
        TimeSpan requestWindow,
        int requestLimit,
        CancellationToken cancellationToken = default)
    {
        var windowStart = now - requestWindow;
        var resetWindow = _context.Users
            .Where(user =>
                user.Id == userId &&
                user.Role == UserRole.Admin &&
                (user.AdminPasswordResetRequestWindowStart == null ||
                 user.AdminPasswordResetRequestWindowStart <= windowStart ||
                 user.AdminPasswordResetRequestCount < requestLimit));

        var updated = await resetWindow.ExecuteUpdateAsync(update => update
            .SetProperty(user => user.AdminPasswordResetCodeHash, codeHash)
            .SetProperty(user => user.AdminPasswordResetCodeExpiry, expiresAt)
            .SetProperty(user => user.AdminPasswordResetAttempts, 0)
            .SetProperty(
                user => user.AdminPasswordResetRequestCount,
                user => user.AdminPasswordResetRequestWindowStart == null ||
                        user.AdminPasswordResetRequestWindowStart <= windowStart
                    ? 1
                    : user.AdminPasswordResetRequestCount + 1)
            .SetProperty(
                user => user.AdminPasswordResetRequestWindowStart,
                user => user.AdminPasswordResetRequestWindowStart == null ||
                        user.AdminPasswordResetRequestWindowStart <= windowStart
                    ? now
                    : user.AdminPasswordResetRequestWindowStart),
            cancellationToken);

        return updated == 1;
    }

    public async Task<bool> IncrementAdminPasswordResetAttemptsAsync(
        Guid userId,
        DateTime now,
        int attemptLimit,
        CancellationToken cancellationToken = default)
    {
        var updated = await _context.Users
            .Where(user =>
                user.Id == userId &&
                user.Role == UserRole.Admin &&
                user.AdminPasswordResetCodeHash != null &&
                user.AdminPasswordResetCodeExpiry > now &&
                user.AdminPasswordResetAttempts < attemptLimit)
            .ExecuteUpdateAsync(update => update
                .SetProperty(
                    user => user.AdminPasswordResetAttempts,
                    user => user.AdminPasswordResetAttempts + 1)
                .SetProperty(
                    user => user.AdminPasswordResetCodeHash,
                    user => user.AdminPasswordResetAttempts + 1 >= attemptLimit
                        ? null
                        : user.AdminPasswordResetCodeHash)
                .SetProperty(
                    user => user.AdminPasswordResetCodeExpiry,
                    user => user.AdminPasswordResetAttempts + 1 >= attemptLimit
                        ? null
                        : user.AdminPasswordResetCodeExpiry),
                cancellationToken);

        return updated == 1;
    }

    public async Task<bool> CompleteAdminPasswordResetAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        string passwordHash,
        int attemptLimit,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var updated = await _context.Users
                .Where(user =>
                    user.Id == userId &&
                    user.Role == UserRole.Admin &&
                    user.AdminPasswordResetCodeHash == codeHash &&
                    user.AdminPasswordResetCodeExpiry > now &&
                    user.AdminPasswordResetAttempts < attemptLimit)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(user => user.PasswordHash, passwordHash)
                    .SetProperty(user => user.AuthTokenVersion, user => user.AuthTokenVersion + 1)
                    .SetProperty(user => user.AdminPasswordResetCodeHash, (string?)null)
                    .SetProperty(user => user.AdminPasswordResetCodeExpiry, (DateTime?)null)
                    .SetProperty(user => user.AdminPasswordResetAttempts, 0)
                    .SetProperty(user => user.PasswordResetToken, (string?)null)
                    .SetProperty(user => user.PasswordResetTokenExpiry, (DateTime?)null)
                    .SetProperty(user => user.FailedLoginCount, 0)
                    .SetProperty(user => user.LockoutUntil, (DateTime?)null)
                    .SetProperty(user => user.LoginFailCount, 0)
                    .SetProperty(user => user.LoginBannedUntil, (DateTime?)null),
                    cancellationToken);

            if (updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await _context.RefreshTokens
                .Where(token => token.UserId == userId && !token.IsRevoked)
                .ExecuteUpdateAsync(update => update.SetProperty(token => token.IsRevoked, true), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
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
    public async Task<DriverProfile?> GetDriverProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.DriverProfiles
            .Include(dp => dp.Cars)
            .Include(dp => dp.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(dp => dp.UserId == userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DriverProfile?> GetDriverProfileByIdAsync(
        Guid driverProfileId,
        CancellationToken cancellationToken = default)
    {
        return await _context.DriverProfiles
            .Include(dp => dp.Cars)
            .Include(dp => dp.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(dp => dp.Id == driverProfileId, cancellationToken);
    }

    public async Task<(List<DriverProfile> Items, int TotalCount)> GetPendingLivenessReviewsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DriverProfiles
            .AsNoTracking()
            .Include(profile => profile.User)
            .Where(profile => !profile.LivenessVerified
                              && profile.ProfilePhotoPath != null
                              && profile.PhotoUpdatedAt != null);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(profile => profile.PhotoUpdatedAt)
            .ThenBy(profile => profile.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<DriverProfile> CreateDriverProfileAsync(DriverProfile profile)
    {
        _context.DriverProfiles.Add(profile);
        await _context.SaveChangesAsync();
        return profile;
    }

    /// <inheritdoc />
    public async Task UpdateDriverProfileAsync(DriverProfile profile, CancellationToken cancellationToken = default)
    {
        _context.DriverProfiles.Update(profile);
        await _context.SaveChangesAsync(cancellationToken);
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
    public async Task CreateRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeRefreshTokenAsync(
        string hashedToken,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var consumed = await _context.RefreshTokens
            .Where(rt => rt.Token == hashedToken && !rt.IsRevoked && rt.ExpiresAt >= utcNow)
            .ExecuteUpdateAsync(
                update => update.SetProperty(rt => rt.IsRevoked, true),
                cancellationToken);

        return consumed == 1;
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
