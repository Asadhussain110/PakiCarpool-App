using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.Models;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Issues completed passenger and driver sessions with an access token and persisted refresh token.
/// </summary>
public interface IUserSessionService
{
    Task<AuthResponseDto> IssueAsync(User user, CancellationToken cancellationToken = default);
}
