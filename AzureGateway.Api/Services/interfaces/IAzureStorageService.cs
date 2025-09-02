using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IAzureStorageService
    {
        Task<bool> IsConnectedAsync();
        Task<UploadResult> UploadFileAsync(string filePath, string containerName, string? blobName = null, IProgress<AzureUploadProgress>? progress = null);
        Task<UploadResult> UploadDataAsync(byte[] data, string fileName, string containerName, string? blobName = null);
        Task<bool> BlobExistsAsync(string containerName, string blobName);
        Task<bool> DeleteBlobAsync(string containerName, string blobName);
        Task<IEnumerable<string>> ListBlobsAsync(string containerName, string? prefix = null);
        Task<bool> CreateContainerIfNotExistsAsync(string containerName);
        Task<AzureStorageInfo> GetStorageInfoAsync();
    }
}
