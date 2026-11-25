using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Domain.Models;
using PakiCarpool.Repository.EFCore.ValueConverters;

namespace PakiCarpool.Repository.EFCore;

/// <summary>
/// Main EF Core database context for Paki Carpool.
/// All entity configurations applied via Fluent API from the Configurations folder.
/// Encryption converters for sensitive fields applied in OnModelCreating.
/// </summary>
public class PakiCarpoolDbContext : DbContext
{
    private readonly Dictionary<byte, string> _encryptionKeys = new();
    private readonly string? _legacyCbcKey;
    private readonly byte _activeEncryptionVersion;

    public PakiCarpoolDbContext(DbContextOptions<PakiCarpoolDbContext> options)
        : base(options)
    {
    }

    public PakiCarpoolDbContext(DbContextOptions<PakiCarpoolDbContext> options, IConfiguration configuration)
        : base(options)
    {
        var legacyKey = configuration["AES_ENCRYPTION_KEY"];
        if (string.IsNullOrWhiteSpace(legacyKey))
            legacyKey = null;
        var versionOneKey = legacyKey ?? configuration["AES_KEY_V1"];
        var versionTwoKey = configuration["AES_KEY_V2"];
        if (string.IsNullOrWhiteSpace(versionTwoKey))
            versionTwoKey = null;

        if (!string.IsNullOrWhiteSpace(legacyKey))
            _encryptionKeys[0] = legacyKey;
        if (!string.IsNullOrWhiteSpace(versionOneKey))
            _encryptionKeys[1] = versionOneKey;
        if (!string.IsNullOrWhiteSpace(versionTwoKey))
            _encryptionKeys[2] = versionTwoKey;

        if (_encryptionKeys.Count > 0)
        {
            _activeEncryptionVersion = _encryptionKeys.Keys.Max();
            _legacyCbcKey = new[] { legacyKey, versionOneKey, versionTwoKey }
                .First(value => !string.IsNullOrWhiteSpace(value));
        }
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<DriverProfile> DriverProfiles => Set<DriverProfile>();
    public DbSet<DriverCar> DriverCars => Set<DriverCar>();
    public DbSet<Ride> Rides => Set<Ride>();
    public DbSet<RideStop> RideStops => Set<RideStop>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<DriverLocation> DriverLocations => Set<DriverLocation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TotpSecret> TotpSecrets => Set<TotpSecret>();
    public DbSet<CancellationLog> CancellationLogs => Set<CancellationLog>();
    public DbSet<NoShowLog> NoShowLogs => Set<NoShowLog>();
    public DbSet<Flag> Flags => Set<Flag>();
    public DbSet<LivenessChallenge> LivenessChallenges => Set<LivenessChallenge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all IEntityTypeConfiguration classes from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PakiCarpoolDbContext).Assembly);

        // Apply AES-256 encryption value converters for sensitive fields
        if (_encryptionKeys.Count > 0 && _legacyCbcKey is not null)
        {
            var converter = new EncryptedStringConverter(
                _encryptionKeys,
                _activeEncryptionVersion,
                _legacyCbcKey);
            var licensePlateConverter = new EncryptedStringConverter(
                _encryptionKeys,
                _activeEncryptionVersion,
                _legacyCbcKey,
                tolerateDoubleEncryption: true);

            modelBuilder.Entity<DriverCar>()
                .Property(e => e.LicensePlate)
                .HasConversion(licensePlateConverter);

            modelBuilder.Entity<TotpSecret>()
                .Property(e => e.Secret)
                .HasConversion(converter);
        }
    }
}
