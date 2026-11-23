using PakiCarpool.Domain.DTOs.Places;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Provides address suggestions through the internal Photon geocoder.
/// </summary>
public interface IPlacesService
{
    /// <summary>
    /// Searches Photon for address suggestions matching the query.
    /// </summary>
    Task<IEnumerable<PlaceSuggestionDto>> AutocompleteAsync(
        string query,
        int limit,
        string? language = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverse geocodes a coordinate to a place suggestion.
    /// </summary>
    Task<PlaceSuggestionDto?> ReverseAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}