using PakiCarpool.Domain.DTOs.Common;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines flagging/reporting operations for both drivers and passengers.
/// Flags are reviewed and resolved by admins.
/// </summary>
public interface IFlagService
{
    /// <summary>
    /// Creates a flag/report against another user for a specific ride.
    /// Status defaults to Open. Admin resolves as Dismissed or ActionTaken.
    /// </summary>
    Task<ApiResponse<object>> CreateFlagAsync(Guid reporterId, FlagDto dto, CancellationToken cancellationToken = default);
}
