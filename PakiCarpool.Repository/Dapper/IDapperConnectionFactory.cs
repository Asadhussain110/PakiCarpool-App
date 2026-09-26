using System.Data;

namespace PakiCarpool.Repository.Dapper;

/// <summary>
/// Factory interface for creating IDbConnection instances for Dapper queries.
/// Registered as singleton in DI. Dapper query classes use this to create
/// properly-scoped connections that are disposed after each query.
/// </summary>
public interface IDapperConnectionFactory
{
    /// <summary>
    /// Creates a new database connection. Caller is responsible for disposing.
    /// </summary>
    IDbConnection CreateConnection();
}
