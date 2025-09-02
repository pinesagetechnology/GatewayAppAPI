using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IUploadProcessorService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task<bool> IsRunningAsync();
        Task<UploadProcessorStatus> GetStatusAsync();
        Task ProcessPendingUploadsAsync(int maxConcurrent = 3);
        Task RetryFailedUploadsAsync();
        Task PauseProcessingAsync();
        Task ResumeProcessingAsync();
    }
}
