using Microsoft.AspNetCore.SignalR;

namespace CubeRacing.Infrastructure.Hubs;

public class GameHub : Hub
{
    public async Task JoinSession(string sessionId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public async Task LeaveSession(string sessionId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}
