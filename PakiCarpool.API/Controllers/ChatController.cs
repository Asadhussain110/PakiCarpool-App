using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PakiCarpool.API.Helpers;
using PakiCarpool.Domain.DTOs.Chat;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Chat history endpoints. Real-time messaging is handled via SignalR.
/// Base route: /api/v1/chat
/// </summary>
[Route("api/v1/chat")]
[Authorize(Policy = RolePolicy.PassengerOrDriverPolicy)]
public sealed class ChatController : ApiControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>GET /chat/{bookingId}/messages — Chat history (7-day window).</summary>
    [HttpGet("{bookingId:guid}/messages")]
    [ProducesResponseType(typeof(ApiResponse<ChatHistoryResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMessages(Guid bookingId)
        => FromResponse(await _chatService.GetChatHistoryAsync(GetUserId(), bookingId));
}
