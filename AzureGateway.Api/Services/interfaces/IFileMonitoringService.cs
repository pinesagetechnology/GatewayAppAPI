using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IFileMonitoringService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task<bool> IsRunningAsync();
        FileMonitoringStatus GetStatusAsync();

        Task RefreshDataSourcesAsync();
    }

}