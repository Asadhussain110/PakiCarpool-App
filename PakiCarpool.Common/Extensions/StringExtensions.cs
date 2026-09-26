namespace PakiCarpool.Common.Extensions;

/// <summary>
/// String utility extensions. Prevents scattered null checks and ad-hoc formatting.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Returns true if the string is null, empty, or whitespace-only.
    /// </summary>
    public static bool IsNullOrWhitespace(this string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Truncates a string to the specified max length, appending "..." if truncated.
    /// </summary>
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    /// <summary>
    /// Masks an email address for display (e.g., "u***@example.com").
    /// </summary>
    public static string MaskEmail(this string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            return email;

        var parts = email.Split('@');
        var local = parts[0];
        var masked = local.Length > 1
            ? local[0] + new string('*', Math.Min(local.Length - 1, 5))
            : local;
        return $"{masked}@{parts[1]}";
    }

    /// <summary>
    /// Converts a string to lowercase and trims whitespace. Useful for email normalization.
    /// </summary>
    public static string NormalizeEmail(this string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
