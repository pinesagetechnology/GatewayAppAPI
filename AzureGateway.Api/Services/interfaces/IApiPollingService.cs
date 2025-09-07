using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IApiPollingService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task<bool> IsRunningAsync();
        Task<ApiPollingStatus> GetStatusAsync();
        Task RefreshDataSourcesAsync();
        Task StartDataSourceAsync(int dataSourceId);
        Task StopDataSourceAsync(int dataSourceId);
    }
}

