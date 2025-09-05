using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IApiPollingService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task<bool> IsRunningAsync();
        ApiPollingStatus GetStatusAsync();
        Task RefreshDataSourcesAsync();
    }

    public class ApiPollingStatus
    {
        public bool IsRunning { get; set; }
        public DateTime StartedAt { get; set; }
        public int ActiveApiPollers { get; set; }
        public long TotalItemsProcessed { get; set; }
        public DateTime? LastActivity { get; set; }
        public List<DataSourceStatus> DataSources { get; set; } = new();
    }
}

