using Microsoft.AspNetCore.SignalR;

// A SignalR hub: clients connect at the pattern MapHub is given and invoke the hub's public methods by name.
namespace CSharp.Endpoints.Chat
{
    public abstract class RoomHub : Hub
    {
        // Inherited: an invocable method too.
        public Task Ping() => Task.CompletedTask;
    }

    public class ChatHub : RoomHub
    {
        public async Task Send(string message)
        {
            await File.AppendAllTextAsync("chat.log", message);
            await Clients.All.SendAsync("received", message);
        }

        [HubMethodName("leave")]
        public Task LeaveRoom() => Groups.RemoveFromGroupAsync(Context.ConnectionId, "room");

        // The platform's: not invoked by clients.
        public override Task OnConnectedAsync() => base.OnConnectedAsync();

        private static string Clean(string message) => message.Trim();
    }
}
