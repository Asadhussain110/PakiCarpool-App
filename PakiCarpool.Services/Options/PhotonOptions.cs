namespace PakiCarpool.Services.Options;

public sealed class PhotonOptions
{
    public const string SectionName = "Photon";

    public string BaseUrl { get; set; } = string.Empty;
    public int DefaultLimit { get; set; }
}