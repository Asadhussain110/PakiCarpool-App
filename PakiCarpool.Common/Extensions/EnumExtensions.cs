namespace PakiCarpool.Common.Extensions;

/// <summary>
/// Enum utility extensions for display name conversion and safe parsing.
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Converts an enum value to its string name. Returns the raw name.
    /// </summary>
    public static string ToDisplayName<T>(this T enumValue) where T : struct, Enum
    {
        return enumValue.ToString();
    }

    /// <summary>
    /// Attempts to parse a string to the specified enum type. Returns null on failure.
    /// </summary>
    public static T? TryParseEnum<T>(this string value) where T : struct, Enum
    {
        return Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;
    }

    /// <summary>
    /// Returns all values of the specified enum type.
    /// </summary>
    public static IEnumerable<T> GetAllValues<T>() where T : struct, Enum
    {
        return Enum.GetValues<T>();
    }
}
