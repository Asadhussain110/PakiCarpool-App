using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;

namespace PakiCarpool.Interfaces.IServices;

public interface IAdminBootstrapService
{
    /// <summary>
    /// Creates the first admin and its verified TOTP enrollment when the configured seed token is valid.
    /// </summary>
    Task<ApiResponse<AdminBootstrapResponseDto>> BootstrapAsync(
        AdminBootstrapRequestDto dto,
        string? seedToken,
        CancellationToken cancellationToken = default);
}
