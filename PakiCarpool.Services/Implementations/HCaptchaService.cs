using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Calls the hCaptcha siteverify endpoint to validate a front-end CAPTCHA token.
/// Reads the secret key from configuration key "HCaptcha:SecretKey".
/// </summary>
public sealed class HCaptchaService : IHCaptchaService
{
    private const string SiteverifyUrl = "https://hcaptcha.com/siteverify";

    private readonly HttpClient _http;
    private readonly string _secretKey;

    public HCaptchaService(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _secretKey = configuration["HCaptcha:SecretKey"]
            ?? throw new InvalidOperationException(
                "hCaptcha secret key is not configured. Set HCaptcha:SecretKey in appsettings.json or as an environment variable.");
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("secret",   _secretKey),
            new KeyValuePair<string, string>("response", token),
        });

        try
        {
            var response = await _http.PostAsync(SiteverifyUrl, form);
            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<HCaptchaVerifyResponse>();
            return result?.Success is true;
        }
        catch
        {
            // Network or parse failure — treat as invalid to stay secure.
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private response DTO
    // ──────────────────────────────────────────────────────────────────────────
    private sealed record HCaptchaVerifyResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("success")]
        bool Success
    );
}
