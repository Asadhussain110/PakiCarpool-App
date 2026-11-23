namespace PakiCarpool.Interfaces.IServices;

public interface ITokenRevocationService
{
    Task RevokeTokenAsync(string jti, TimeSpan? ttl = null);
    Task<bool> IsRevokedAsync(string jti);
}
