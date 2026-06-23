using CubeRacing.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace CubeRacing.API.Controllers;

[ApiController]
[Route("api/players")]
public class PlayersController : ControllerBase
{
    private readonly CreatePlayer _createPlayer;
    public PlayersController(CreatePlayer createPlayer) => _createPlayer = createPlayer;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlayerRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Nickname))
            return BadRequest(new { error = "Nickname is required." });

        var result = await _createPlayer.ExecuteAsync(req, ct);
        return Ok(result);
    }
}
