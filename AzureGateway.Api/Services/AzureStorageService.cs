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
        private BlobServiceClient? _blobServiceClient;
        private readonly IConfigurationService _configService;
        private readonly ILogger<AzureStorageService> _logger;
        private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);

        public AzureStorageService(IConfigurationService configService, ILogger<AzureStorageService> logger)
        {
            _configService = configService;
            _logger = logger;
            _logger.LogDebug("AzureStorageService initialized");
        }

        private async Task<BlobServiceClient?> GetBlobServiceClientAsync()
        {
            if (_blobServiceClient != null)
            {
                _logger.LogDebug("Using existing BlobServiceClient instance");
                return _blobServiceClient;
            }

            _logger.LogDebug("Initializing new BlobServiceClient...");
            try
            {
                var connectionString = await _configService.GetValueAsync("Azure.StorageConnectionString");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _logger.LogDebug("Azure Storage connection string found, creating BlobServiceClient");
                    _blobServiceClient = new BlobServiceClient(connectionString);
                    _logger.LogInformation("BlobServiceClient initialized successfully");
                    return _blobServiceClient;
                }
                else
                {
                    _logger.LogWarning("Azure Storage connection string is not configured");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Storage client");
                return null;
            }
        }

        public async Task<bool> IsConnectedAsync()
        {
            _logger.LogDebug("Testing Azure Storage connection...");
            var blobServiceClient = await GetBlobServiceClientAsync();
            if (blobServiceClient == null)
            {
                _logger.LogWarning("Cannot test connection - BlobServiceClient is null");
                return false;
            }

            await _connectionSemaphore.WaitAsync();
            try
            {
                _logger.LogDebug("Attempting to get Azure Storage account properties...");
                var properties = await blobServiceClient.GetPropertiesAsync();
                _logger.LogInformation("Azure Storage connection test successful - Account: {AccountName}", 
                    blobServiceClient.AccountName);
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

            _logger.LogInformation("Starting file upload: {FilePath} to container {Container}", filePath, containerName);

            try
            {
                var blobServiceClient = await GetBlobServiceClientAsync();
                if (blobServiceClient == null)
                {
                    result.ErrorMessage = "Azure Storage client is not initialized";
                    _logger.LogError("Upload failed: Azure Storage client is not initialized");
                    return result;
                }

                if (!File.Exists(filePath))
                {
                    result.ErrorMessage = $"File not found: {filePath}";
                    _logger.LogError("Upload failed: File not found at {FilePath}", filePath);
                    return result;
                }

                // Ensure container exists
                _logger.LogDebug("Ensuring container {Container} exists...", containerName);
                await CreateContainerIfNotExistsAsync(containerName);

                // Generate blob name if not provided
                blobName ??= GenerateBlobName(Path.GetFileName(filePath));
                _logger.LogDebug("Using blob name: {BlobName}", blobName);

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                // Get file info
                var fileInfo = new FileInfo(filePath);
                var totalBytes = fileInfo.Length;
                _logger.LogInformation("File size: {FileSize} bytes ({FileSizeMB:F2} MB)", 
                    totalBytes, totalBytes / (1024.0 * 1024.0));

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
                    _logger.LogDebug("Set content type: {ContentType}", contentType);
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
                _logger.LogInformation("Starting upload to Azure Blob Storage...");
                using var fileStream = File.OpenRead(filePath);
                var response = await blobClient.UploadAsync(fileStream, uploadOptions);

                stopwatch.Stop();

                result.IsSuccess = true;
                result.BlobUrl = blobClient.Uri.ToString();
                result.UploadedBytes = totalBytes;
                result.Duration = stopwatch.Elapsed;
                result.ETag = response.Value.ETag.ToString();

                _logger.LogInformation("File upload completed successfully: {FileName} -> {BlobUrl} in {Duration}ms ({Speed:F2} MB/s)",
                    Path.GetFileName(filePath), result.BlobUrl, stopwatch.ElapsedMilliseconds, 
                    (totalBytes / (1024.0 * 1024.0)) / (stopwatch.Elapsed.TotalSeconds));

                return result;
            }
            catch (RequestFailedException ex)
            {
                stopwatch.Stop();
                result.ErrorMessage = $"Azure Storage error: {ex.ErrorCode} - {ex.Message}";
                _logger.LogError(ex, "Azure Storage upload failed for {FilePath}: {ErrorCode} - {Message}", 
                    filePath, ex.ErrorCode, ex.Message);
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

            _logger.LogInformation("Starting data upload: {FileName} ({DataSize} bytes) to container {Container}", 
                fileName, data.Length, containerName);

            try
            {
                var blobServiceClient = await GetBlobServiceClientAsync();
                if (blobServiceClient == null)
                {
                    result.ErrorMessage = "Azure Storage client is not initialized";
                    _logger.LogError("Data upload failed: Azure Storage client is not initialized");
                    return result;
                }

                // Ensure container exists
                _logger.LogDebug("Ensuring container {Container} exists...", containerName);
                await CreateContainerIfNotExistsAsync(containerName);

                // Generate blob name if not provided
                blobName ??= GenerateBlobName(fileName);
                _logger.LogDebug("Using blob name: {BlobName}", blobName);

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
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
                _logger.LogInformation("Starting data upload to Azure Blob Storage...");
                using var dataStream = new MemoryStream(data);
                var response = await blobClient.UploadAsync(dataStream, uploadOptions);

                stopwatch.Stop();

                result.IsSuccess = true;
                result.BlobUrl = blobClient.Uri.ToString();
                result.UploadedBytes = data.Length;
                result.Duration = stopwatch.Elapsed;
                result.ETag = response.Value.ETag.ToString();

                _logger.LogInformation("Data upload completed successfully: {FileName} -> {BlobUrl} in {Duration}ms ({Speed:F2} MB/s)",
                    fileName, result.BlobUrl, stopwatch.ElapsedMilliseconds, 
                    (data.Length / (1024.0 * 1024.0)) / (stopwatch.Elapsed.TotalSeconds));

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
            _logger.LogDebug("Checking if blob exists: {Container}/{BlobName}", containerName, blobName);
            try
            {
                var blobServiceClient = await GetBlobServiceClientAsync();
                if (blobServiceClient == null)
                {
                    _logger.LogWarning("Cannot check blob existence - BlobServiceClient is null");
                    return false;
                }

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);
                var response = await blobClient.ExistsAsync();
                
                _logger.LogDebug("Blob {Container}/{BlobName} exists: {Exists}", containerName, blobName, response.Value);
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
            _logger.LogInformation("Deleting blob: {Container}/{BlobName}", containerName, blobName);
            try
            {
                var blobServiceClient = await GetBlobServiceClientAsync();
                if (blobServiceClient == null)
                {
                    _logger.LogWarning("Cannot delete blob - BlobServiceClient is null");
                    return false;
                }

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();
                
                _logger.LogInformation("Successfully deleted blob: {Container}/{BlobName}", containerName, blobName);
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
            _logger.LogDebug("Listing blobs in container: {Container} (prefix: {Prefix})", containerName, prefix ?? "None");
            try
            {
                var blobServiceClient = await GetBlobServiceClientAsync();
                if (blobServiceClient == null)
                {
                    _logger.LogWarning("Cannot list blobs - BlobServiceClient is null");
                    return Enumerable.Empty<string>();
                }

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobs = new List<string>();

                await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
                {
                    blobs.Add(blobItem.Name);
                }

                _logger.LogDebug("Found {Count} blobs in container {Container}", blobs.Count, containerName);
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
            _logger.LogDebug("Creating container if not exists: {ContainerName}", containerName);
            try
            {
                var blobServiceClient = await GetBlobServiceClientAsync();
                if (blobServiceClient == null)
                {
                    _logger.LogWarning("Cannot create container - BlobServiceClient is null");
                    return false;
                }

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var response = await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
                
                if (response != null)
                {
                    _logger.LogInformation("Created new container: {ContainerName}", containerName);
                }
                else
                {
                    _logger.LogDebug("Container already exists: {ContainerName}", containerName);
                }
                
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
            _logger.LogDebug("Getting Azure Storage information...");
            var info = new AzureStorageInfo();

            try
            {
                var blobServiceClient = await GetBlobServiceClientAsync();
                if (blobServiceClient == null)
                {
                    info.ErrorMessage = "Azure Storage client is not initialized";
                    _logger.LogWarning("Cannot get storage info - BlobServiceClient is null");
                    return info;
                }

                // Test connection
                _logger.LogDebug("Testing connection by getting account properties...");
                var properties = await blobServiceClient.GetPropertiesAsync();
                info.IsConnected = true;
                info.AccountName = blobServiceClient.AccountName;
                _logger.LogInformation("Azure Storage account connected: {AccountName}", info.AccountName);

                // List containers
                _logger.LogDebug("Listing containers...");
                var containers = new List<string>();
                await foreach (var container in blobServiceClient.GetBlobContainersAsync())
                {
                    containers.Add(container.Name);
                }
                info.Containers = containers;
                
                _logger.LogInformation("Found {Count} containers in Azure Storage account", containers.Count);

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
