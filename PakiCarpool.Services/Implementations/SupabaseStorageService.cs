using PakiCarpool.Interfaces.IServices;
using Supabase.Storage;

namespace PakiCarpool.Services.Implementations;

public sealed class SupabaseStorageService : IStorageService
{
    private readonly Supabase.Client _client;
    private readonly Lazy<Task> _initialization;

    public SupabaseStorageService(Supabase.Client client)
    {
        _client = client;
        _initialization = new Lazy<Task>(() => _client.InitializeAsync());
    }

    public async Task<string> UploadAsync(
        string containerName,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            throw new InvalidOperationException("Storage container name is required.");

        if (content.Length == 0 || content.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("File payload is empty or exceeds the 5 MB limit.");

        if (!string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only JPEG and PNG uploads are permitted.");
        }

        await _initialization.Value.WaitAsync(cancellationToken);

        var bucket = _client.Storage.From(containerName);
        var objectPath = fileName;
        await bucket.Upload(content, objectPath, new Supabase.Storage.FileOptions { Upsert = true })
            .WaitAsync(cancellationToken);

        return bucket.GetPublicUrl(objectPath, null, null);
    }

    public async Task DeleteAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return;
        }

        await _initialization.Value.WaitAsync(cancellationToken);
        string bucketName;
        string objectPath;
        if (Uri.TryCreate(fileKey, UriKind.Absolute, out var publicUrl))
        {
            var segments = publicUrl.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var objectSegment = Array.IndexOf(segments, "object");
            if (objectSegment < 0 || segments.Length <= objectSegment + 3)
                throw new InvalidOperationException("Storage object URL is invalid.");

            bucketName = Uri.UnescapeDataString(segments[objectSegment + 2]);
            objectPath = string.Join('/', segments.Skip(objectSegment + 3).Select(Uri.UnescapeDataString));
        }
        else
        {
            var separator = fileKey.IndexOf('/');
            if (separator <= 0 || separator == fileKey.Length - 1)
                throw new InvalidOperationException("Storage object key is invalid.");
            bucketName = fileKey[..separator];
            objectPath = fileKey[(separator + 1)..];
        }

        var bucket = _client.Storage.From(bucketName);
        await bucket.Remove(objectPath).WaitAsync(cancellationToken);
    }

    public async Task<string> UploadDriverSelfieAsync(
        Guid driverId,
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes.Length == 0 || imageBytes.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("File payload is empty or exceeds the 5 MB limit.");

        var extension = contentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            _ => throw new InvalidOperationException("Only JPEG and PNG uploads are permitted.")
        };
        var fileName = $"{driverId:D}/profile-{Guid.NewGuid():N}.{extension}";
        return await UploadAsync("driver-selfies", fileName, imageBytes, contentType, cancellationToken);
    }

    public async Task<string> UploadPrivateCarDocumentAsync(
        Guid driverId,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ValidateImage(content, contentType);
        await _initialization.Value.WaitAsync(cancellationToken);

        var extension = contentType == "image/jpeg" ? "jpg" : "png";
        var objectPath = $"{driverId:D}/{Guid.NewGuid():N}.{extension}";
        var bucket = _client.Storage.From("driver-registration-documents");
        await bucket.Upload(content, objectPath, new Supabase.Storage.FileOptions { Upsert = false })
            .WaitAsync(cancellationToken);

        return $"private://driver-registration-documents/{objectPath}";
    }

    public async Task<string> UploadCarPhotoAsync(
        Guid driverId,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ValidateImage(content, contentType);
        await _initialization.Value.WaitAsync(cancellationToken);

        var extension = contentType == "image/jpeg" ? "jpg" : "png";
        var objectPath = $"{driverId:D}/{Guid.NewGuid():N}.{extension}";
        var bucket = _client.Storage.From("driver-cars");
        await bucket.Upload(content, objectPath, new Supabase.Storage.FileOptions { Upsert = false })
            .WaitAsync(cancellationToken);

        return $"private://driver-cars/{objectPath}";
    }

    public string GetCarPhotoUrl(string photoPath)
    {
        const string prefix = "private://driver-cars/";
        if (!photoPath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Car photo storage key is invalid.");

        var objectPath = photoPath[prefix.Length..];
        if (objectPath.Split('/').Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidOperationException("Car photo storage key is invalid.");
        }

        return _client.Storage.From("driver-cars").GetPublicUrl(objectPath, null, null);
    }

    public async Task<string> CreatePrivateCarDocumentSignedUrlAsync(
        string documentKey,
        CancellationToken cancellationToken = default)
    {
        const string prefix = "private://driver-registration-documents/";
        if (!documentKey.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Private car document key is invalid.");

        var objectPath = documentKey[prefix.Length..];
        if (objectPath.Split('/').Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidOperationException("Private car document key is invalid.");

        await _initialization.Value.WaitAsync(cancellationToken);
        var bucket = _client.Storage.From("driver-registration-documents");
        return await bucket.CreateSignedUrl(objectPath, 300)
            .WaitAsync(cancellationToken);
    }

    private static void ValidateImage(byte[] content, string contentType)
    {
        if (content.Length == 0 || content.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("File payload is empty or exceeds the 5 MB limit.");
        if (contentType is not "image/jpeg" and not "image/png")
            throw new InvalidOperationException("Only JPEG and PNG uploads are permitted.");
    }
}