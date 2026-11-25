namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Validates the canonical, driver-scoped storage keys emitted by the car upload endpoints.
/// </summary>
internal static class CarStoragePath
{
    internal const string RegistrationBucket = "driver-registration-documents";
    internal const string PhotoBucket = "driver-cars";

    public static bool IsRegistrationPath(string? path, Guid driverUserId) =>
        IsCanonicalPath(path, driverUserId, RegistrationBucket);

    public static bool IsPhotoPath(string? path, Guid driverUserId) =>
        IsCanonicalPath(path, driverUserId, PhotoBucket);

    private static bool IsCanonicalPath(string? path, Guid driverUserId, string expectedBucket)
    {
        if (path is null || path.Length > 256 || path.IndexOfAny(['%', '\\', '?', '#']) >= 0)
            return false;

        var prefix = $"private://{expectedBucket}/{driverUserId:D}/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var fileName = path[prefix.Length..];
        if (fileName.Length != 36 || fileName.Contains('/'))
            return false;

        var extension = Path.GetExtension(fileName);
        if (extension is not (".jpg" or ".png"))
            return false;

        return Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out var fileId)
               && string.Equals(fileName, $"{fileId:N}{extension}", StringComparison.Ordinal);
    }
}
