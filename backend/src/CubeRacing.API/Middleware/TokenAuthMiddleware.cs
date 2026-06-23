using CubeRacing.Domain.Interfaces;

namespace CubeRacing.API.Middleware;

public class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    public TokenAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IPlayerRepository players)
    {
        var header = ctx.Request.Headers["Authorization"].FirstOrDefault();
        if (header?.StartsWith("Bearer ") == true &&
            Guid.TryParse(header["Bearer ".Length..], out var token))
        {
            var player = await players.GetByTokenAsync(token, ctx.RequestAborted);
            if (player is not null) ctx.Items["Player"] = player;
        }
        await _next(ctx);
    }
}
