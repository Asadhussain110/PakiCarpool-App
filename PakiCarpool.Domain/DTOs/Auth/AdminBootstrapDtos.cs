using System.ComponentModel.DataAnnotations;

namespace PakiCarpool.Domain.DTOs.Auth;

public sealed class AdminBootstrapRequestDto
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}

public sealed class AdminBootstrapResponseDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string TotpSecret { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
}
