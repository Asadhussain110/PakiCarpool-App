using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
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
    private const string HCaptchaTestSecret = "0x0000000000000000000000000000000000000000";

    private readonly HttpClient _http;
    private readonly string _secretKey;

    public HCaptchaService(
        HttpClient http,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _http = http;
        var configuredSecret = configuration["HCaptcha:SecretKey"];
        if (string.IsNullOrWhiteSpace(configuredSecret))
            configuredSecret = Environment.GetEnvironmentVariable("HCAPTCHA_SECRET");
        if (string.IsNullOrWhiteSpace(configuredSecret)
            && string.Equals(environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
            configuredSecret = HCaptchaTestSecret;
        _secretKey = !string.IsNullOrWhiteSpace(configuredSecret)
            ? configuredSecret
            : throw new InvalidOperationException(
                "hCaptcha secret key is not configured. Set HCaptcha:SecretKey or HCAPTCHA_SECRET.");
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
