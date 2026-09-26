using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PakiCarpool.Repository.EFCore;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Reads DATABASE_URL from environment variable. Never hardcodes credentials.
/// Usage: dotnet ef migrations add [Name] --project PakiCarpool.Repository
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PakiCarpoolDbContext>
{
    public PakiCarpoolDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException(
                "DATABASE_URL environment variable is not set. " +
                "Set it before running migrations: " +
                "Host=localhost;Port=5432;Database=pakicarpool;Username=postgres;Password=YOUR_PASSWORD");

        var builder = new DbContextOptionsBuilder<PakiCarpoolDbContext>();
        builder.UseNpgsql(connectionString);

        return new PakiCarpoolDbContext(builder.Options);
    }
}
