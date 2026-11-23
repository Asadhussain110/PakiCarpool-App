using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PakiCarpool.Repository.EFCore;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Reads the same DB_CONNECTION_STRING used by the runtime so both app and tooling
/// target the same Supabase Postgres database.
/// Usage: dotnet ef migrations add [Name] --project PakiCarpool.Repository
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PakiCarpoolDbContext>
{
    public PakiCarpoolDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "DB_CONNECTION_STRING environment variable is not set. " +
                "Set it before running migrations.");

        var builder = new DbContextOptionsBuilder<PakiCarpoolDbContext>();
        builder.UseNpgsql(connectionString);

        return new PakiCarpoolDbContext(builder.Options);
    }
}
