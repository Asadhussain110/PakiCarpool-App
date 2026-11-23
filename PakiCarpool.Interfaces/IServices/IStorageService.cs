namespace PakiCarpool.Interfaces.IServices;

public interface IStorageService
{
    Task<string> UploadAsync(string containerName, string fileName, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<string> UploadPrivateCarDocumentAsync(Guid driverId, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<string> CreatePrivateCarDocumentSignedUrlAsync(string documentKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string fileKey, CancellationToken cancellationToken = default);
    Task<string> UploadDriverSelfieAsync(Guid driverId, byte[] imageBytes, string contentType, CancellationToken cancellationToken = default);
}