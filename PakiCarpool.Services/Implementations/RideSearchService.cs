using Microsoft.Extensions.Options;
using PakiCarpool.Domain.DTOs.Passenger;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Services.Options;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Finds coordinate-matched ride segments and ranks them by match quality and distance.
/// </summary>
public sealed class RideSearchService : IRideSearchService
{
    private readonly IRideRepository _rideRepository;
    private readonly RideSearchOptions _options;

    public RideSearchService(
        IRideRepository rideRepository,
        IOptions<RideSearchOptions> options)
    {
        _rideRepository = rideRepository;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RideSearchResultDto>> SearchRidesAsync(RideSearchRequestDto request)
    {
        var candidates = await _rideRepository.GetCandidateRidesAsync(
            request.StartLatitude,
            request.StartLongitude,
            request.EndLatitude,
            request.EndLongitude,
            request.RadiusMeters);

        var rankedResults = new List<(RideSearchResultDto Result, double CombinedDistance)>();

        foreach (var rideCandidates in candidates.GroupBy(candidate => candidate.RideId))
        {
            var startStop = rideCandidates
                .Where(candidate => candidate.StartDistanceMeters <= request.RadiusMeters)
                .OrderBy(candidate => candidate.StartDistanceMeters)
                .ThenBy(candidate => candidate.SequenceOrder)
                .FirstOrDefault();
            var endStop = rideCandidates
                .Where(candidate => candidate.EndDistanceMeters <= request.RadiusMeters)
                .OrderBy(candidate => candidate.EndDistanceMeters)
                .ThenByDescending(candidate => candidate.SequenceOrder)
                .FirstOrDefault();

            if (startStop is null || endStop is null || endStop.SequenceOrder <= startStop.SequenceOrder)
                continue;

            var availableSeats = startStop.AvailableSeats;
            if (availableSeats <= 0)
                continue;

            var startIsPerfect = startStop.StartDistanceMeters <= _options.PerfectMatchToleranceMeters;
            var endIsPerfect = endStop.EndDistanceMeters <= _options.PerfectMatchToleranceMeters;
            var matchRank = startIsPerfect
                ? endIsPerfect ? 1 : 2
                : endIsPerfect ? 2 : 3;

            var photoUrl = startStop.DriverPhotoPath ?? string.Empty;
            if (photoUrl.Length > 0 && startStop.DriverPhotoUpdatedAt.HasValue)
                photoUrl = $"{photoUrl}?v={startStop.DriverPhotoUpdatedAt.Value.Ticks}";

            rankedResults.Add((new RideSearchResultDto
            {
                RideId = startStop.RideId,
                DriverId = startStop.DriverId,
                DriverName = startStop.DriverName,
                DriverPhotoUrl = photoUrl,
                StartStop = ToStopMatch(startStop, startStop.StartDistanceMeters, startIsPerfect),
                EndStop = ToStopMatch(endStop, endStop.EndDistanceMeters, endIsPerfect),
                MatchRank = matchRank,
                DeparturTime = startStop.DepartureTime,
                AvailableSeats = availableSeats
            }, startStop.StartDistanceMeters + endStop.EndDistanceMeters));
        }

        return rankedResults
            .OrderBy(match => match.Result.MatchRank)
            .ThenBy(match => match.CombinedDistance)
            .Select(match => match.Result)
            .ToList();
    }

    private static RideStopMatchDto ToStopMatch(
        RideSearchCandidateDto candidate,
        double distanceMeters,
        bool isPerfectMatch)
    {
        return new RideStopMatchDto
        {
            StopId = candidate.StopId,
            StopName = candidate.StopName,
            Latitude = candidate.Latitude,
            Longitude = candidate.Longitude,
            IsPerfectMatch = isPerfectMatch,
            DistanceMeters = isPerfectMatch ? null : distanceMeters
        };
    }
}