using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace CubeRacing.API.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly GetCurrentSession _getCurrent;
    private readonly PlaceBet _placeBet;
    private readonly ICurrentSessionStore _store;

    public SessionsController(GetCurrentSession getCurrent, PlaceBet placeBet, ICurrentSessionStore store)
    {
        _getCurrent = getCurrent;
        _placeBet = placeBet;
        _store = store;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var session = await _getCurrent.ExecuteAsync(ct);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpPost("{sessionId:guid}/bets")]
    public async Task<IActionResult> PlaceBet(Guid sessionId, [FromBody] PlaceBetBody body, CancellationToken ct)
    {
        var player = HttpContext.Items["Player"] as Player;
        if (player is null) return Unauthorized();

        var req = new PlaceBetRequest(player.Id, sessionId, body.NpcId, body.Amount);
        var result = await _placeBet.ExecuteAsync(req, ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                PlaceBetError.BettingClosed => Conflict(new { error = "Betting is closed." }),
                PlaceBetError.AlreadyBet => Conflict(new { error = "You have already placed a bet this session." }),
                PlaceBetError.InsufficientChips => BadRequest(new { error = "Insufficient chips." }),
                PlaceBetError.InvalidNpcId => BadRequest(new { error = "Invalid NPC ID." }),
                PlaceBetError.InvalidAmount => BadRequest(new { error = "Amount must be greater than zero." }),
                _ => NotFound(new { error = "Session not found." })
            };
        }

        return Ok(new { success = true });
    }
}

public record PlaceBetBody(int NpcId, int Amount);
