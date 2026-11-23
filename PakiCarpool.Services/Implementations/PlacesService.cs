using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PakiCarpool.Domain.DTOs.Places;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Services.Options;

namespace PakiCarpool.Services.Implementations;

public sealed class PlacesUnavailableException : Exception
{
    public PlacesUnavailableException(Exception? innerException = null)
        : base("Photon place search is unavailable.", innerException)
    {
    }
}

/// <summary>
/// Proxies address autocomplete requests to the private Photon instance.
/// </summary>
public sealed class PlacesService : IPlacesService
{
    private readonly HttpClient _httpClient;
    private readonly PhotonOptions _options;

    public PlacesService(HttpClient httpClient, IOptions<PhotonOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<PlaceSuggestionDto>> AutocompleteAsync(
        string query,
        int limit,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var requestUri = $"{_options.BaseUrl.TrimEnd('/')}/api/?q={Uri.EscapeDataString(query)}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(language))
            requestUri += $"&lang={Uri.EscapeDataString(language.Trim())}";

        using var document = await GetDocumentAsync(requestUri, cancellationToken);
        if (document is null)
            return Array.Empty<PlaceSuggestionDto>();

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PlaceSuggestionDto>();
        }

        var suggestions = new List<PlaceSuggestionDto>();
        foreach (var feature in features.EnumerateArray())
        {
            if (TryMapFeature(feature, out var suggestion) &&
                TryGetProperties(feature, out var properties) &&
                string.Equals(ReadString(properties, "countrycode"), "PK", StringComparison.OrdinalIgnoreCase))
                suggestions.Add(suggestion);
        }

        return suggestions;
    }

    /// <inheritdoc />
    public async Task<PlaceSuggestionDto?> ReverseAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var requestUri = $"{_options.BaseUrl.TrimEnd('/')}/reverse?lat={latitude.ToString(CultureInfo.InvariantCulture)}&lon={longitude.ToString(CultureInfo.InvariantCulture)}";
        using var document = await GetDocumentAsync(requestUri, cancellationToken);
        if (document is null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
        {
            return null;
        }

        return TryMapFeature(features[0], out var suggestion) ? suggestion : null;
    }

    private async Task<JsonDocument?> GetDocumentAsync(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new PlacesUnavailableException();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new PlacesUnavailableException(exception);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlacesUnavailableException();
        }
        catch (JsonException exception)
        {
            throw new PlacesUnavailableException(exception);
        }
    }

    private static bool TryMapFeature(JsonElement feature, out PlaceSuggestionDto suggestion)
    {
        suggestion = new PlaceSuggestionDto();
        if (!TryGetProperties(feature, out var properties) ||
            !feature.TryGetProperty("geometry", out var geometry) ||
            geometry.ValueKind != JsonValueKind.Object ||
            !geometry.TryGetProperty("coordinates", out var coordinates) ||
            coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() < 2 ||
            !coordinates[0].TryGetDouble(out var longitude) ||
            !coordinates[1].TryGetDouble(out var latitude) ||
            !double.IsFinite(latitude) || !double.IsFinite(longitude))
        {
            return false;
        }

        var name = ReadString(properties, "name");
        var displayName = string.Join(", ", new[]
        {
            name,
            ReadString(properties, "city"),
            ReadString(properties, "state"),
            ReadString(properties, "country")
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        suggestion = new PlaceSuggestionDto
        {
            Name = name,
            DisplayName = displayName,
            Latitude = latitude,
            Longitude = longitude
        };
        return true;
    }

    private static bool TryGetProperties(JsonElement feature, out JsonElement properties)
    {
        properties = default;
        return feature.ValueKind == JsonValueKind.Object &&
               feature.TryGetProperty("properties", out properties) &&
               properties.ValueKind == JsonValueKind.Object;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }
}