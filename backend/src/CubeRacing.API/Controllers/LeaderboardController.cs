using CubeRacing.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace CubeRacing.API.Controllers;

[ApiController]
[Route("api/leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly GetLeaderboard _getLeaderboard;
    public LeaderboardController(GetLeaderboard getLeaderboard) => _getLeaderboard = getLeaderboard;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await _getLeaderboard.ExecuteAsync(ct));
}
