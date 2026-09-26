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
    private readonly string? _encryptionKey;

    public PakiCarpoolDbContext(DbContextOptions<PakiCarpoolDbContext> options)
        : base(options)
    {
    }

    public PakiCarpoolDbContext(DbContextOptions<PakiCarpoolDbContext> options, IConfiguration configuration)
        : base(options)
    {
        _encryptionKey = configuration["AES_ENCRYPTION_KEY"];
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all IEntityTypeConfiguration classes from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PakiCarpoolDbContext).Assembly);

        // Apply AES-256 encryption value converters for sensitive fields
        if (!string.IsNullOrEmpty(_encryptionKey))
        {
            var converter = new EncryptedStringConverter(_encryptionKey);

            modelBuilder.Entity<DriverCar>()
                .Property(e => e.LicensePlate)
                .HasConversion(converter);

            modelBuilder.Entity<TotpSecret>()
                .Property(e => e.Secret)
                .HasConversion(converter);
        }
    }
}
