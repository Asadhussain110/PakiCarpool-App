namespace PakiCarpool.Security.Helpers;

/// <summary>
/// Server-side password composition policy shared by registration and password reset.
/// The character-class definitions intentionally mirror the Flutter validator in
/// auth_screens.dart exactly (ASCII [A-Z], [a-z], [0-9], and [^A-Za-z0-9]) so the
/// mobile app and the API can never disagree about what a valid password is.
/// A "symbol" is any character that is not an ASCII letter or digit — spaces and
/// Unicode letters therefore count as symbols, exactly as in the Flutter regex.
/// Length is counted in UTF-16 characters (never bytes), matching Dart's String.length.
/// This policy is enforced only when a password is set (register and reset) —
/// never on login, so users with older passwords can still sign in.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>Minimum password length in characters.</summary>
    public const int MinimumLength = 8;

    /// <summary>Maximum password length in characters.</summary>
    public const int MaximumLength = 128;

    /// <summary>
    /// Lists every composition requirement the password does not satisfy.
    /// The result is empty when the password is compliant.
    /// The password is never trimmed or altered by this check.
    /// </summary>
    /// <param name="password">The exact password value submitted by the client.</param>
    /// <returns>One entry per unmet requirement, in a stable order.</returns>
    public static IReadOnlyList<string> GetMissingRequirements(string password)
    {
        var missing = new List<string>();

        if (password.Length < MinimumLength)
            missing.Add($"be at least {MinimumLength} characters");
        if (password.Length > MaximumLength)
            missing.Add($"be at most {MaximumLength} characters");
        if (!ContainsUpperCase(password))
            missing.Add("contain an uppercase letter");
        if (!ContainsLowerCase(password))
            missing.Add("contain a lowercase letter");
        if (!ContainsDigit(password))
            missing.Add("contain a number");
        if (!ContainsSymbol(password))
            missing.Add("contain a symbol");

        return missing;
    }

    /// <summary>
    /// Builds the client-facing validation message that lists every missing
    /// password requirement at once. Returns null when the password is compliant.
    /// The message never echoes or describes the submitted password value.
    /// </summary>
    /// <param name="password">The exact password value submitted by the client.</param>
    /// <returns>The full requirement message, or null when compliant.</returns>
    public static string? GetValidationMessage(string password)
    {
        var missing = GetMissingRequirements(password);
        return missing.Count == 0
            ? null
            : $"Password must {string.Join(", ", missing)}.";
    }

    /// <summary>
    /// Returns true when the password satisfies every composition requirement.
    /// </summary>
    /// <param name="password">The exact password value submitted by the client.</param>
    public static bool IsCompliant(string password) => GetMissingRequirements(password).Count == 0;

    private static bool ContainsUpperCase(string password)
    {
        foreach (var character in password)
            if (character is >= 'A' and <= 'Z')
                return true;
        return false;
    }

    private static bool ContainsLowerCase(string password)
    {
        foreach (var character in password)
            if (character is >= 'a' and <= 'z')
                return true;
        return false;
    }

    private static bool ContainsDigit(string password)
    {
        foreach (var character in password)
            if (character is >= '0' and <= '9')
                return true;
        return false;
    }

    /// <summary>
    /// A symbol is any character that is not an ASCII letter or digit —
    /// identical to the Flutter validator's [^A-Za-z0-9] class.
    /// </summary>
    private static bool ContainsSymbol(string password)
    {
        foreach (var character in password)
            if (character is (< 'A' or > 'Z')
                and (< 'a' or > 'z')
                and (< '0' or > '9'))
                return true;
        return false;
    }
}
