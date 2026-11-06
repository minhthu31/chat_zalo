using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.Hubs;

// Notifications only. Clients send messages through the HTTP API and cannot
// select arbitrary conversation groups or supply another user's identity.
[Authorize]
public sealed class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || id == Guid.Empty)
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }
}
