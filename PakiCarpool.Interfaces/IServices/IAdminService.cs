using PakiCarpool.Domain.DTOs.Admin;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines all admin portal business operations.
/// Covers dashboard, user management, car approvals, ride oversight, flag resolution, and reports.
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Returns summary dashboard metrics (total users, rides, pending approvals, etc.).
    /// </summary>
    Task<ApiResponse<DashboardDto>> GetDashboardAsync();

    /// <summary>
    /// Lists all users with pagination and optional filtering.
    /// </summary>
    Task<ApiResponse<PaginatedResult<UserListItemDto>>> GetUsersAsync(PaginationParams pagination);

    /// <summary>
    /// Returns full user profile details including driver profile and cars if applicable.
    /// </summary>
    Task<ApiResponse<UserDetailDto>> GetUserDetailAsync(Guid userId);

    /// <summary>
    /// Blocks a user account. Sets IsActive = false.
    /// </summary>
    Task<ApiResponse<object>> BlockUserAsync(Guid userId, BlockUserDto dto);

    /// <summary>
    /// Unblocks a user account. Sets IsActive = true, IsSuspended = false.
    /// </summary>
    Task<ApiResponse<object>> UnblockUserAsync(Guid userId);

    /// <summary>
    /// Lists all pending car registrations awaiting admin approval.
    /// </summary>
    Task<ApiResponse<List<CarApprovalDto>>> GetPendingCarApprovalsAsync();

    Task<ApiResponse<PaginatedResult<LivenessReviewDto>>> GetPendingLivenessReviewsAsync(
        PaginationParams pagination,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a car registration. Sets status to Approved with timestamp.
    /// </summary>
    Task<ApiResponse<object>> ApproveCarAsync(Guid carId);

    /// <summary>
    /// Rejects a car registration with a reason.
    /// </summary>
    Task<ApiResponse<object>> RejectCarAsync(Guid carId, RejectCarDto dto);

    Task<ApiResponse<object>> RejectLivenessSubmissionAsync(
        Guid driverId,
        RejectLivenessDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all rides with pagination.
    /// </summary>
    Task<ApiResponse<PaginatedResult<RideResponseDto>>> GetRidesAsync(PaginationParams pagination);

    /// <summary>
    /// Admin force-cancels any ride.
    /// </summary>
    Task<ApiResponse<object>> CancelRideAsync(Guid rideId);

    /// <summary>
    /// Lists all flags with pagination.
    /// </summary>
    Task<ApiResponse<PaginatedResult<FlagListItemDto>>> GetFlagsAsync(PaginationParams pagination);

    /// <summary>
    /// Dismisses a flag with optional notes.
    /// </summary>
    Task<ApiResponse<object>> DismissFlagAsync(Guid flagId, DismissFlagDto dto);

    /// <summary>
    /// Takes action on a flag (e.g., suspend user, issue warning).
    /// </summary>
    Task<ApiResponse<object>> ActionFlagAsync(Guid flagId, ActionFlagDto dto);

    /// <summary>
    /// Generates admin reports with optional date filtering.
    /// </summary>
    Task<ApiResponse<object>> GetReportsAsync(DateTime? from, DateTime? to);

    /// <summary>
    /// Exports reports as CSV or PDF.
    /// </summary>
    Task<byte[]> ExportReportsAsync(DateTime? from, DateTime? to, string format);
}
