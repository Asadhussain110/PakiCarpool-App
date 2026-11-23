namespace PakiCarpool.Interfaces.IServices;

public interface IOAuthStateStore
{
    Task<bool> TryCreateAsync(
        string state,
        string role,
        string codeVerifier,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<OAuthStateRecord?> ConsumeAsync(string state, CancellationToken cancellationToken = default);
}

public sealed record OAuthStateRecord(string Role, string CodeVerifier, DateTime ExpiresAtUtc);