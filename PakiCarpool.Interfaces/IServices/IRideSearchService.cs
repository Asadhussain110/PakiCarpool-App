using PakiCarpool.Domain.DTOs.Passenger;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Searches rides using passenger start and end coordinates.
/// </summary>
public interface IRideSearchService
{
    /// <summary>
    /// Finds available rides with valid upcoming stop pairs and remaining segment seats.
    /// </summary>
    Task<IEnumerable<RideSearchResultDto>> SearchRidesAsync(RideSearchRequestDto request);
}