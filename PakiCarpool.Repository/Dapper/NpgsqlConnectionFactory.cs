using System.Data;
using Npgsql;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Npgsql implementation of IDapperConnectionFactory.
/// Creates NpgsqlConnection instances from the configured connection string.
/// Registered as singleton in API/Program.cs via DI.
/// </summary>
public class NpgsqlConnectionFactory : IDapperConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Creates a new NpgsqlConnection. The connection is NOT opened — Dapper opens it automatically.
    /// Caller must dispose via using statement.
    /// </summary>
    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }
}
