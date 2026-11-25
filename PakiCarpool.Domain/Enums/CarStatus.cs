namespace PakiCarpool.Domain.Enums;

public enum CarStatus
{
    Pending,
    Approved,
    Rejected
}

public enum CarApprovalOutcome
{
    Approved,
    NotPending,
    OriginalUnavailable
}
