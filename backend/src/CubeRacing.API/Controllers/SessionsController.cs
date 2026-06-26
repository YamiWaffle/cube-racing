using System.Text.Json;
using CubeRacing.Application.GameEngine;
using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Entities;
using CubeRacing.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CubeRacing.API.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly GetCurrentSession    _getCurrent;
    private readonly PlaceBet             _placeBet;
    private readonly ICurrentSessionStore _store;
    private readonly IGameRoundRepository _roundRepo;

    public SessionsController(GetCurrentSession getCurrent, PlaceBet placeBet,
        ICurrentSessionStore store, IGameRoundRepository roundRepo)
    {
        _getCurrent = getCurrent;
        _placeBet   = placeBet;
        _store      = store;
        _roundRepo  = roundRepo;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var session = await _getCurrent.ExecuteAsync(ct);
        return session is null ? NotFound() : Ok(session);
    }

    // Returns the current NPC positions (SquareStacks from the latest saved round).
    // Frontend calls this on race scene entry to teleport cubes to their correct squares.
    [HttpGet("current/squares")]
    public async Task<IActionResult> GetCurrentSquares(CancellationToken ct)
    {
        var sessionId = _store.CurrentSessionId;
        if (sessionId is null) return NotFound();

        var latest = await _roundRepo.GetLatestBySessionAsync(sessionId.Value, ct);
        if (latest is null) return NoContent();

        var result = JsonSerializer.Deserialize<RoundResult>(latest.MovementDataJson);
        return result?.SquareStacks is null ? NoContent() : Ok(result.SquareStacks);
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
                PlaceBetError.BettingClosed    => Conflict(new { error = "Betting is closed." }),
                PlaceBetError.AlreadyBet       => Conflict(new { error = "You have already placed a bet this session." }),
                PlaceBetError.InsufficientChips => BadRequest(new { error = "Insufficient chips." }),
                PlaceBetError.InvalidNpcId     => BadRequest(new { error = "Invalid NPC ID." }),
                PlaceBetError.InvalidAmount    => BadRequest(new { error = "Amount must be greater than zero." }),
                _                              => NotFound(new { error = "Session not found." })
            };
        }

        return Ok(new { success = true });
    }
}

public record PlaceBetBody(int NpcId, int Amount);
