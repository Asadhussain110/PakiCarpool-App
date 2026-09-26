using PakiCarpool.Domain.Models;

namespace PakiCarpool.Interfaces.IRepositories;

/// <summary>
/// Repository for User entity CRUD and query operations.
/// All database operations go through this interface — no direct DbContext calls in services.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by their primary key.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id);

    /// <summary>
    /// Gets a user by email (case-insensitive).
    /// </summary>
    Task<User?> GetByEmailAsync(string email);

    /// <summary>
    /// Gets a user by their Google ID.
    /// </summary>
    Task<User?> GetByGoogleIdAsync(string googleId);

    /// <summary>
    /// Checks if an email is already registered.
    /// </summary>
    Task<bool> EmailExistsAsync(string email);

    /// <summary>
    /// Creates a new user record.
    /// </summary>
    Task<User> CreateAsync(User user);

    /// <summary>
    /// Updates an existing user record.
    /// </summary>
    Task UpdateAsync(User user);

    /// <summary>
    /// Gets all users with pagination.
    /// </summary>
    Task<(List<User> Items, int TotalCount)> GetAllPaginatedAsync(int page, int pageSize);

    /// <summary>
    /// Gets the count of users by role.
    /// </summary>
    Task<int> CountByRoleAsync(Domain.Enums.UserRole role);

    /// <summary>
    /// Gets the count of suspended users.
    /// </summary>
    Task<int> CountSuspendedAsync();

    /// <summary>
    /// Gets the driver profile for a user, including cars.
    /// </summary>
    Task<DriverProfile?> GetDriverProfileByUserIdAsync(Guid userId);

    /// <summary>
    /// Gets a driver profile by its primary key.
    /// </summary>
    Task<DriverProfile?> GetDriverProfileByIdAsync(Guid driverProfileId);

    /// <summary>
    /// Creates a new driver profile.
    /// </summary>
    Task<DriverProfile> CreateDriverProfileAsync(DriverProfile profile);

    /// <summary>
    /// Updates an existing driver profile.
    /// </summary>
    Task UpdateDriverProfileAsync(DriverProfile profile);

    /// <summary>
    /// Gets a driver car by ID.
    /// </summary>
    Task<DriverCar?> GetDriverCarByIdAsync(Guid carId);

    /// <summary>
    /// Gets all cars for a driver.
    /// </summary>
    Task<List<DriverCar>> GetDriverCarsAsync(Guid driverProfileId);

    /// <summary>
    /// Gets all cars with a specific status (e.g., Pending for admin approval).
    /// </summary>
    Task<List<DriverCar>> GetCarsByStatusAsync(Domain.Enums.CarStatus status);

    /// <summary>
    /// Creates a new driver car record.
    /// </summary>
    Task<DriverCar> CreateDriverCarAsync(DriverCar car);

    /// <summary>
    /// Updates an existing driver car record.
    /// </summary>
    Task UpdateDriverCarAsync(DriverCar car);

    /// <summary>
    /// Gets a refresh token by its hashed value.
    /// </summary>
    Task<RefreshToken?> GetRefreshTokenAsync(string hashedToken);

    /// <summary>
    /// Creates a new refresh token.
    /// </summary>
    Task CreateRefreshTokenAsync(RefreshToken token);

    /// <summary>
    /// Updates a refresh token (e.g., to revoke it).
    /// </summary>
    Task UpdateRefreshTokenAsync(RefreshToken token);

    /// <summary>
    /// Revokes all refresh tokens for a user.
    /// </summary>
    Task RevokeAllRefreshTokensAsync(Guid userId);

    /// <summary>
    /// Deletes expired and revoked refresh tokens (cleanup job).
    /// </summary>
    Task CleanupRefreshTokensAsync();

    /// <summary>
    /// Gets a TOTP secret for a user.
    /// </summary>
    Task<TotpSecret?> GetTotpSecretAsync(Guid userId);

    /// <summary>
    /// Creates a new TOTP secret.
    /// </summary>
    Task CreateTotpSecretAsync(TotpSecret secret);

    /// <summary>
    /// Gets the count of frivolous cancellations for a user.
    /// </summary>
    Task<int> CountFrivolousCancellationsAsync(Guid userId);

    /// <summary>
    /// Gets the count of no-shows for a user as offender.
    /// </summary>
    Task<int> CountNoShowsAsync(Guid userId);

    /// <summary>
    /// Creates a cancellation log entry.
    /// </summary>
    Task CreateCancellationLogAsync(CancellationLog log);

    /// <summary>
    /// Creates a no-show log entry.
    /// </summary>
    Task CreateNoShowLogAsync(NoShowLog log);
}
