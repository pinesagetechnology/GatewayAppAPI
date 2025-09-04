using Microsoft.AspNetCore.SignalR;

namespace AzureGateway.Api.Hubs
{
    public class UploadStatusHub : Hub
    {
        private readonly ILogger<UploadStatusHub> _logger;

        public UploadStatusHub(ILogger<UploadStatusHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected to UploadStatusHub: {ConnectionId} from {IPAddress}", 
                Context.ConnectionId, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogWarning(exception, "Client disconnected from UploadStatusHub with exception: {ConnectionId}", 
                    Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Client disconnected from UploadStatusHub: {ConnectionId}", Context.ConnectionId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinGroup(string groupName)
        {
            _logger.LogInformation("Client {ConnectionId} joining group: {GroupName}", Context.ConnectionId, groupName);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Client {ConnectionId} successfully joined group: {GroupName}", Context.ConnectionId, groupName);
        }

        public async Task LeaveGroup(string groupName)
        {
            _logger.LogInformation("Client {ConnectionId} leaving group: {GroupName}", Context.ConnectionId, groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Client {ConnectionId} successfully left group: {GroupName}", Context.ConnectionId, groupName);
        }

        public async Task JoinUploadGroup()
        {
            _logger.LogInformation("Client {ConnectionId} joining upload updates group", Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, "UploadUpdates");
            _logger.LogInformation("Client {ConnectionId} successfully joined upload updates group", Context.ConnectionId);
        }

        public async Task JoinProcessorGroup()
        {
            _logger.LogInformation("Client {ConnectionId} joining processor updates group", Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, "ProcessorUpdates");
            _logger.LogInformation("Client {ConnectionId} successfully joined processor updates group", Context.ConnectionId);
        }

        public async Task LeaveUploadGroup()
        {
            _logger.LogInformation("Client {ConnectionId} leaving upload updates group", Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "UploadUpdates");
            _logger.LogInformation("Client {ConnectionId} successfully left upload updates group", Context.ConnectionId);
        }

        public async Task LeaveProcessorGroup()
        {
            _logger.LogInformation("Client {ConnectionId} leaving processor updates group", Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "ProcessorUpdates");
            _logger.LogInformation("Client {ConnectionId} successfully left processor updates group", Context.ConnectionId);
        }

        public async Task SendUploadUpdate(string message)
        {
            _logger.LogDebug("Client {ConnectionId} sending upload update: {Message}", Context.ConnectionId, message);
            await Clients.Group("UploadUpdates").SendAsync("ReceiveUploadUpdate", message);
            _logger.LogDebug("Upload update sent to upload updates group: {Message}", message);
        }

        public async Task SendProcessorUpdate(string message)
        {
            _logger.LogDebug("Client {ConnectionId} sending processor update: {Message}", Context.ConnectionId, message);
            await Clients.Group("ProcessorUpdates").SendAsync("ReceiveProcessorUpdate", message);
            _logger.LogDebug("Processor update sent to processor updates group: {Message}", message);
        }
    }
}