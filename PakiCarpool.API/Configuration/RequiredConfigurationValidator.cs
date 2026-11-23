using System.Globalization;
using System.Text;

namespace PakiCarpool.API.Configuration;

public static class RequiredConfigurationValidator
{
    private const string HCaptchaTestSecret = "0x0000000000000000000000000000000000000000";

    private static readonly string[] RequiredKeys =
    [
        "DB_CONNECTION_STRING",
        "JWT_SECRET",
        "JWT_ISSUER",
        "JWT_AUDIENCE",
        "SMTP_HOST",
        "SMTP_PORT",
        "SMTP_USER",
        "SMTP_PASS",
        "SMTP_FROM",
        "GOOGLE_CLIENT_ID",
        "GOOGLE_CLIENT_SECRET",
        "APP_DOMAIN",
        "SUPABASE_URL",
        "SUPABASE_KEY",
        "ADMIN_SEED_TOKEN"
    ];
    private static readonly string[] DevelopmentOptionalKeys =
    [
        "GOOGLE_CLIENT_ID",
        "GOOGLE_CLIENT_SECRET",
        "APP_DOMAIN"
    ];

    public static void Validate(IConfiguration configuration, bool requireRedis = false)
    {
        var missing = new List<string>();

        var keysToValidate = requireRedis
            ? RequiredKeys.Append("REDIS_CONNECTION_STRING")
            : RequiredKeys.Except(DevelopmentOptionalKeys);

        foreach (var key in keysToValidate)
        {
            var configuredValue = configuration[key];
            var envValue = Environment.GetEnvironmentVariable(key);

            if (string.IsNullOrWhiteSpace(configuredValue) && string.IsNullOrWhiteSpace(envValue))
            {
                missing.Add(key);
                continue;
            }

            var smtpPort = configuration["SMTP_PORT"];
            if (string.IsNullOrWhiteSpace(smtpPort))
                smtpPort = Environment.GetEnvironmentVariable("SMTP_PORT");
            if (!string.IsNullOrWhiteSpace(smtpPort)
                && (!int.TryParse(smtpPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                    || port is < 1 or > 65535))
            {
                throw new InvalidOperationException("SMTP_PORT must be a valid TCP port between 1 and 65535.");
            }

            var value = string.IsNullOrWhiteSpace(configuredValue) ? envValue! : configuredValue;
            if (IsExampleValue(value))
                missing.Add(key);
        }

        var hCaptchaSecret = configuration["HCaptcha:SecretKey"];
        if (string.IsNullOrWhiteSpace(hCaptchaSecret))
            hCaptchaSecret = Environment.GetEnvironmentVariable("HCAPTCHA_SECRET");
        if (requireRedis &&
            (string.IsNullOrWhiteSpace(hCaptchaSecret) ||
             string.Equals(hCaptchaSecret, HCaptchaTestSecret, StringComparison.Ordinal)))
        {
            missing.Add("HCAPTCHA_SECRET (set a non-test value outside Development)");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Missing required configuration values: {0}. Set them in environment variables or appsettings before starting the API.",
                    string.Join(", ", missing)));
        }

        var aesKeyNames = new[] { "AES_KEY_V2", "AES_KEY_V1", "AES_ENCRYPTION_KEY" };
        var configuredAesKeys = aesKeyNames
            .Select(keyName => (Name: keyName, Value: configuration[keyName]))
            .Where(key => !string.IsNullOrWhiteSpace(key.Value))
            .ToList();
        if (!configuredAesKeys.Any(key => key.Name is "AES_KEY_V1" or "AES_ENCRYPTION_KEY"))
            throw new InvalidOperationException("The existing AES decryption key is required. Set AES_KEY_V1 or AES_ENCRYPTION_KEY.");

        foreach (var (name, value) in configuredAesKeys)
        {
            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(value!);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{name} must be a Base64-encoded 256-bit key.");
            }

            if (keyBytes.Length != 32)
                throw new InvalidOperationException($"{name} must be a Base64-encoded 256-bit key.");
        }

        var jwtSecret = configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
            throw new InvalidOperationException("JWT_SECRET must contain at least 32 UTF-8 bytes.");
    }

    private static bool IsExampleValue(string value)
        => value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
           || value.Contains("SET_", StringComparison.OrdinalIgnoreCase)
           || value.Contains("your-project", StringComparison.OrdinalIgnoreCase)
           || value.Equals("your-app-password", StringComparison.OrdinalIgnoreCase)
           || value.Equals("you@gmail.com", StringComparison.OrdinalIgnoreCase);
}
