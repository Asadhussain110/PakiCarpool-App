using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.Models;

/// <summary>
/// User-submitted flag/report against another user.
/// Admin resolves as Dismissed or ActionTaken.
/// </summary>
public class Flag
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid ReporteeId { get; set; }
    public Guid RideId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public FlagStatus Status { get; set; } = FlagStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
