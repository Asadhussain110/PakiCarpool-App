namespace PakiCarpool.Domain.DTOs.Places;

public sealed class PlaceSuggestionDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}