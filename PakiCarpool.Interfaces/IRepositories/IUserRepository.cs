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
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by email (case-insensitive).
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by their Google ID.
    /// </summary>
    Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an email is already registered.
    /// </summary>
    Task<bool> EmailExistsAsync(string email);

    /// <summary>
    /// Creates a new user record.
    /// </summary>
    Task<User> CreateAsync(User user);

    /// <summary>
    /// Creates a user and optional driver profile in one transaction.
    /// </summary>
    Task<User> CreateWithDriverProfileAsync(User user, DriverProfile? driverProfile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates the first admin and TOTP secret only if no admin exists.
    /// </summary>
    Task<bool> CreateFirstAdminAsync(User user, TotpSecret totpSecret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments the user's login failure count.
    /// </summary>
    Task IncrementLoginFailAsync(Guid userId);

    /// <summary>
    /// Resets the user's login failure count and clears its login ban.
    /// </summary>
    Task ResetLoginFailAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the persisted login failure count without relying on tracked entity state.
    /// </summary>
    Task<int?> GetLoginFailCountAsync(Guid userId);

    /// <summary>
    /// Sets a login ban and resets the failure count atomically.
    /// </summary>
    Task SetLoginBanAsync(Guid userId, DateTime bannedUntil);

    /// <summary>
    /// Updates an existing user record.
    /// </summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    Task<int?> GetAuthTokenVersionAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> TryIssuePasswordResetCodeAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        DateTime expiresAt,
        TimeSpan requestWindow,
        int requestLimit,
        CancellationToken cancellationToken = default);

    Task<bool> IncrementPasswordResetAttemptsAsync(
        Guid userId,
        DateTime now,
        int attemptLimit,
        CancellationToken cancellationToken = default);

    Task<bool> CompletePasswordResetAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        string passwordHash,
        int attemptLimit,
        CancellationToken cancellationToken = default);

    Task<bool> TryIssueAdminPasswordResetCodeAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        DateTime expiresAt,
        TimeSpan requestWindow,
        int requestLimit,
        CancellationToken cancellationToken = default);

    Task<bool> IncrementAdminPasswordResetAttemptsAsync(
        Guid userId,
        DateTime now,
        int attemptLimit,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAdminPasswordResetAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        string passwordHash,
        int attemptLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically issues a replacement email-verification OTP for an unverified, unlocked account.
    /// Enforces the resend cooldown, the per-window resend limit, and the OTP lock per account,
    /// invalidates the previous code, and resets the attempt counter only when a lock has expired.
    /// Returns false when the account is locked, the cooldown is active, or the window limit is hit.
    /// </summary>
    Task<bool> TryIssueEmailOtpAsync(
        Guid userId,
        string codeHash,
        DateTime now,
        DateTime expiresAt,
        TimeSpan resendCooldown,
        TimeSpan resendWindow,
        int resendLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically counts a wrong email-verification OTP attempt for the account.
    /// When the attempt limit is reached, the account is locked until the supplied
    /// lockout instant and the current code is invalidated.
    /// </summary>
    Task<bool> IncrementEmailOtpAttemptsAsync(
        Guid userId,
        DateTime now,
        int attemptLimit,
        DateTime lockoutUntil,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user that was created but never email-verified, together with its
    /// driver profile. Used to roll back registration when the verification email
    /// cannot be sent so that registering again with the same email works.
    /// Returns false when no unverified user row was deleted.
    /// </summary>
    Task<bool> DeleteUnverifiedUserAsync(Guid userId, CancellationToken cancellationToken = default);

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
    Task<DriverProfile?> GetDriverProfileByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a driver profile by its primary key.
    /// </summary>
    Task<DriverProfile?> GetDriverProfileByIdAsync(Guid driverProfileId, CancellationToken cancellationToken = default);

    Task<(List<DriverProfile> Items, int TotalCount)> GetPendingLivenessReviewsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new driver profile.
    /// </summary>
    Task<DriverProfile> CreateDriverProfileAsync(DriverProfile profile);

    /// <summary>
    /// Updates an existing driver profile.
    /// </summary>
    Task UpdateDriverProfileAsync(DriverProfile profile, CancellationToken cancellationToken = default);

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

    Task<DriverCar?> GetPendingReplacementForCarAsync(Guid originalCarId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically approves a pending car and retires its original for new rides.
    /// </summary>
    Task<Domain.Enums.CarApprovalOutcome> ApproveDriverCarAsync(Guid carId, DateTime approvedAt);

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
    Task CreateRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically consumes a valid refresh token. Returns false when another request already consumed it.
    /// </summary>
    Task<bool> TryConsumeRefreshTokenAsync(
        string hashedToken,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a refresh token (e.g., to revoke it during logout).
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
    /// Gets the most recent no-show recorded against a user as offender.
    /// Used to attribute the admin-review flag raised at the no-show threshold.
    /// </summary>
    Task<NoShowLog?> GetLatestNoShowByOffenderAsync(Guid offenderId);

    /// <summary>
    /// Creates a cancellation log entry.
    /// </summary>
    Task CreateCancellationLogAsync(CancellationLog log);
}
