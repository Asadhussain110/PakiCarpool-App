namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Verifies an hCaptcha response token against the hCaptcha siteverify API.
/// </summary>
public interface IHCaptchaService
{
    /// <summary>
    /// Posts the <paramref name="token"/> to https://hcaptcha.com/siteverify
    /// using the configured secret key and returns <c>true</c> if
    /// hCaptcha reports <c>"success": true</c>.
    /// </summary>
    Task<bool> VerifyAsync(string token);
}
