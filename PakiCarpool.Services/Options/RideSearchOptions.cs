namespace PakiCarpool.Services.Options;

public sealed class RideSearchOptions
{
    public const string SectionName = "RideSearch";

    public double PerfectMatchToleranceMeters { get; set; }
    public double DefaultRadiusMeters { get; set; }
    public double MinRadiusMeters { get; set; }
    public double MaxRadiusMeters { get; set; }
}