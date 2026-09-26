using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Admin;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Admin portal endpoints. Admin accounts are seeded only.
/// Base route: /api/v1/admin
/// </summary>
[Route("api/v1/admin")]
[Authorize(Policy = RolePolicy.AdminPolicy)]
public sealed class AdminController : ApiControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>GET /admin/dashboard — Summary metrics.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard()
        => FromResponse(await _adminService.GetDashboardAsync());

    /// <summary>GET /admin/users — List all users.</summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<UserListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers([FromQuery] PaginationParams pagination)
        => FromResponse(await _adminService.GetUsersAsync(pagination));

    /// <summary>GET /admin/users/{userId} — User full profile.</summary>
    [HttpGet("users/{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUser(Guid userId)
        => FromResponse(await _adminService.GetUserDetailAsync(userId));

    /// <summary>PUT /admin/users/{userId}/block — Block account.</summary>
    [HttpPut("users/{userId:guid}/block")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BlockUser(Guid userId, [FromBody] BlockUserDto dto)
        => FromResponse(await _adminService.BlockUserAsync(userId, dto));

    /// <summary>PUT /admin/users/{userId}/unblock — Unblock account.</summary>
    [HttpPut("users/{userId:guid}/unblock")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnblockUser(Guid userId)
        => FromResponse(await _adminService.UnblockUserAsync(userId));

    /// <summary>GET /admin/driver-approvals — Pending car registrations.</summary>
    [HttpGet("driver-approvals")]
    [ProducesResponseType(typeof(ApiResponse<List<CarApprovalDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDriverApprovals()
        => FromResponse(await _adminService.GetPendingCarApprovalsAsync());

    /// <summary>PUT /admin/driver-approvals/{carId}/approve — Approve car.</summary>
    [HttpPut("driver-approvals/{carId:guid}/approve")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveCar(Guid carId)
        => FromResponse(await _adminService.ApproveCarAsync(carId));

    /// <summary>PUT /admin/driver-approvals/{carId}/reject — Reject car.</summary>
    [HttpPut("driver-approvals/{carId:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectCar(Guid carId, [FromBody] RejectCarDto dto)
        => FromResponse(await _adminService.RejectCarAsync(carId, dto));

    /// <summary>PUT /admin/drivers/{driverId}/manual-verify — Manual liveness approval.</summary>
    [HttpPut("drivers/{driverId:guid}/manual-verify")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ManualVerifyDriver(Guid driverId)
        => FromResponse(await _adminService.ManualVerifyDriverAsync(driverId));

    /// <summary>GET /admin/rides — All rides.</summary>
    [HttpGet("rides")]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<RideResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRides([FromQuery] PaginationParams pagination)
        => FromResponse(await _adminService.GetRidesAsync(pagination));

    /// <summary>DELETE /admin/rides/{rideId} — Cancel any ride.</summary>
    [HttpDelete("rides/{rideId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelRide(Guid rideId)
        => FromResponse(await _adminService.CancelRideAsync(rideId));

    /// <summary>GET /admin/flags — View flags.</summary>
    [HttpGet("flags")]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<FlagListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFlags([FromQuery] PaginationParams pagination)
        => FromResponse(await _adminService.GetFlagsAsync(pagination));

    /// <summary>PUT /admin/flags/{flagId}/dismiss — Dismiss flag.</summary>
    [HttpPut("flags/{flagId:guid}/dismiss")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DismissFlag(Guid flagId, [FromBody] DismissFlagDto dto)
        => FromResponse(await _adminService.DismissFlagAsync(flagId, dto));

    /// <summary>PUT /admin/flags/{flagId}/action — Act on flag.</summary>
    [HttpPut("flags/{flagId:guid}/action")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActionFlag(Guid flagId, [FromBody] ActionFlagDto dto)
        => FromResponse(await _adminService.ActionFlagAsync(flagId, dto));

    /// <summary>GET /admin/reports — Reports.</summary>
    [HttpGet("reports")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReports([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => FromResponse(await _adminService.GetReportsAsync(from, to));

    /// <summary>GET /admin/reports/export — Export CSV/PDF.</summary>
    [HttpGet("reports/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportReports(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "csv")
    {
        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        if (normalized is not ("csv" or "pdf"))
        {
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Format must be 'csv' or 'pdf'."));
        }

        var bytes = await _adminService.ExportReportsAsync(from, to, normalized);
        var contentType = normalized == "pdf" ? "application/pdf" : "text/csv";
        var fileName = $"pakicarpool-report-{DateTime.UtcNow:yyyyMMddHHmmss}.{normalized}";
        return File(bytes, contentType, fileName);
    }
}
