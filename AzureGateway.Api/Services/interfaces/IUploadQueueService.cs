using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IUploadQueueService
    {
        Task<UploadQueue> AddToQueueAsync(string filePath, FileType fileType, DataSource source, long fileSize, string? hash = null);
        Task<IEnumerable<UploadQueue>> GetPendingUploadsAsync();
        Task<IEnumerable<UploadQueue>> GetFailedUploadsAsync();
        Task<UploadQueue?> GetNextUploadAsync();
        Task UpdateStatusAsync(int id, FileStatus status, string? errorMessage = null);
        Task UpdateProgressAsync(int id, long bytesUploaded, long totalBytes, string? statusMessage = null);
        Task MarkAsCompletedAsync(int id, string azureBlobUrl, long uploadDurationMs);
        Task IncrementAttemptAsync(int id, string? errorMessage = null);
        Task ResetFailedUploadsAsync();
        Task<bool> IsDuplicateAsync(string hash);
        Task ArchiveCompletedUploadsAsync(int olderThanDays = 30);
        Task<IEnumerable<UploadQueue>> GetRecentUploadsAsync(int count = 50);
        Task<UploadQueue?> GetUploadByIdAsync(int id);
        Task<object> GetQueueSummaryAsync();
    }
}
