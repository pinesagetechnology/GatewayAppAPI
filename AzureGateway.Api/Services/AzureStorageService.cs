using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure;
using System.Diagnostics;
using AzureGateway.Api.Models;
using Azure.Storage;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class AzureStorageService : IAzureStorageService
    {
        private readonly BlobServiceClient? _blobServiceClient;
        private readonly IConfigurationService _configService;
        private readonly ILogger<AzureStorageService> _logger;
        private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);

        public AzureStorageService(IConfigurationService configService, ILogger<AzureStorageService> logger)
        {
            _configService = configService;
            _logger = logger;

            try
            {
                var connectionString = _configService.GetValueAsync("Azure.StorageConnectionString").Result;
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _blobServiceClient = new BlobServiceClient(connectionString);
                }
                else
                {
                    _logger.LogWarning("Azure Storage connection string is not configured");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Storage client");
            }
        }

        public async Task<bool> IsConnectedAsync()
        {
            if (_blobServiceClient == null)
                return false;

            await _connectionSemaphore.WaitAsync();
            try
            {
                await _blobServiceClient.GetPropertiesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure Storage connection test failed");
                return false;
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        public async Task<UploadResult> UploadFileAsync(string filePath, string containerName, string? blobName = null, IProgress<AzureUploadProgress>? progress = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new UploadResult();

            try
            {
                if (_blobServiceClient == null)
                {
                    result.ErrorMessage = "Azure Storage client is not initialized";
                    return result;
                }

                if (!File.Exists(filePath))
                {
                    result.ErrorMessage = $"File not found: {filePath}";
                    return result;
                }

                // Ensure container exists
                await CreateContainerIfNotExistsAsync(containerName);

                // Generate blob name if not provided
                blobName ??= GenerateBlobName(Path.GetFileName(filePath));

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                // Get file info
                var fileInfo = new FileInfo(filePath);
                var totalBytes = fileInfo.Length;

                // Configure upload options
                var uploadOptions = new BlobUploadOptions
                {
                    TransferOptions = new StorageTransferOptions
                    {
                        InitialTransferSize = 1024 * 1024, // 1MB
                        MaximumTransferSize = 4 * 1024 * 1024, // 4MB chunks
                    },
                    ProgressHandler = progress != null ? new Progress<long>(bytesUploaded =>
                    {
                        progress.Report(new AzureUploadProgress
                        {
                            BytesUploaded = bytesUploaded,
                            TotalBytes = totalBytes,
                            StatusMessage = $"Uploading {Path.GetFileName(filePath)}..."
                        });
                    }) : null
                };

                // Set content type based on file extension
                var contentType = GetContentType(Path.GetExtension(filePath));
                if (!string.IsNullOrEmpty(contentType))
                {
                    uploadOptions.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };
                }

                // Add metadata
                uploadOptions.Metadata = new Dictionary<string, string>
                {
                    ["OriginalFileName"] = Path.GetFileName(filePath),
                    ["UploadedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["FileSizeBytes"] = totalBytes.ToString(),
                    ["Source"] = "AzureGateway"
                };

                // Upload the file
                using var fileStream = File.OpenRead(filePath);
                var response = await blobClient.UploadAsync(fileStream, uploadOptions);

                stopwatch.Stop();

                result.IsSuccess = true;
                result.BlobUrl = blobClient.Uri.ToString();
                result.UploadedBytes = totalBytes;
                result.Duration = stopwatch.Elapsed;
                result.ETag = response.Value.ETag.ToString();

                _logger.LogInformation("Successfully uploaded {FileName} to {BlobUrl} in {Duration}ms",
                    Path.GetFileName(filePath), result.BlobUrl, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (RequestFailedException ex)
            {
                stopwatch.Stop();
                result.ErrorMessage = $"Azure Storage error: {ex.ErrorCode} - {ex.Message}";
                _logger.LogError(ex, "Azure Storage upload failed for {FilePath}", filePath);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.ErrorMessage = $"Upload failed: {ex.Message}";
                _logger.LogError(ex, "File upload failed for {FilePath}", filePath);
                return result;
            }
        }

        public async Task<UploadResult> UploadDataAsync(byte[] data, string fileName, string containerName, string? blobName = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new UploadResult();

            try
            {
                if (_blobServiceClient == null)
                {
                    result.ErrorMessage = "Azure Storage client is not initialized";
                    return result;
                }

                // Ensure container exists
                await CreateContainerIfNotExistsAsync(containerName);

                // Generate blob name if not provided
                blobName ??= GenerateBlobName(fileName);

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                // Configure upload options
                var uploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = GetContentType(Path.GetExtension(fileName))
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["OriginalFileName"] = fileName,
                        ["UploadedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        ["FileSizeBytes"] = data.Length.ToString(),
                        ["Source"] = "AzureGateway"
                    }
                };

                // Upload the data
                using var dataStream = new MemoryStream(data);
                var response = await blobClient.UploadAsync(dataStream, uploadOptions);

                stopwatch.Stop();

                result.IsSuccess = true;
                result.BlobUrl = blobClient.Uri.ToString();
                result.UploadedBytes = data.Length;
                result.Duration = stopwatch.Elapsed;
                result.ETag = response.Value.ETag.ToString();

                _logger.LogInformation("Successfully uploaded data {FileName} to {BlobUrl} in {Duration}ms",
                    fileName, result.BlobUrl, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.ErrorMessage = $"Upload failed: {ex.Message}";
                _logger.LogError(ex, "Data upload failed for {FileName}", fileName);
                return result;
            }
        }

        public async Task<bool> BlobExistsAsync(string containerName, string blobName)
        {
            try
            {
                if (_blobServiceClient == null)
                    return false;

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                var response = await blobClient.ExistsAsync();
                return response.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if blob exists: {ContainerName}/{BlobName}", containerName, blobName);
                return false;
            }
        }

        public async Task<bool> DeleteBlobAsync(string containerName, string blobName)
        {
            try
            {
                if (_blobServiceClient == null)
                    return false;

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                await blobClient.DeleteIfExistsAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blob: {ContainerName}/{BlobName}", containerName, blobName);
                return false;
            }
        }

        public async Task<IEnumerable<string>> ListBlobsAsync(string containerName, string? prefix = null)
        {
            try
            {
                if (_blobServiceClient == null)
                    return Enumerable.Empty<string>();

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobs = new List<string>();

                await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
                {
                    blobs.Add(blobItem.Name);
                }

                return blobs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing blobs in container: {ContainerName}", containerName);
                return Enumerable.Empty<string>();
            }
        }

        public async Task<bool> CreateContainerIfNotExistsAsync(string containerName)
        {
            try
            {
                if (_blobServiceClient == null)
                    return false;

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating container: {ContainerName}", containerName);
                return false;
            }
        }

        public async Task<AzureStorageInfo> GetStorageInfoAsync()
        {
            var info = new AzureStorageInfo();

            try
            {
                if (_blobServiceClient == null)
                {
                    info.ErrorMessage = "Azure Storage client is not initialized";
                    return info;
                }

                // Test connection
                var properties = await _blobServiceClient.GetPropertiesAsync();
                info.IsConnected = true;
                info.AccountName = _blobServiceClient.AccountName;

                // List containers
                var containers = new List<string>();
                await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
                {
                    containers.Add(container.Name);
                }
                info.Containers = containers;

                return info;
            }
            catch (Exception ex)
            {
                info.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error getting Azure Storage info");
                return info;
            }
        }

        private static string GenerateBlobName(string fileName)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy/MM/dd/HH");
            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            var safeFileName = fileName.Replace(" ", "_");
            return $"{timestamp}/{uniqueId}_{safeFileName}";
        }

        private static string GetContentType(string fileExtension)
        {
            return fileExtension.ToLowerInvariant() switch
            {
                ".json" => "application/json",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".xml" => "application/xml",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }
    }
}
