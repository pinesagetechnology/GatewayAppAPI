using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Models;
using AzureGateway.Api.Utilities;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using AzureGateway.Api.Hubs;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class UploadProcessorService : IUploadProcessorService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<UploadStatusHub> _hubContext;
        private readonly ILogger<UploadProcessorService> _logger;

        private readonly ConcurrentDictionary<int, ActiveUploadInfo> _activeUploads = new();
        private readonly ConcurrentQueue<string> _recentErrors = new();
        private readonly SemaphoreSlim _processingLock = new(1, 1);

        private bool _isRunning = false;
        private bool _isPaused = false;
        private DateTime _startedAt;
        private Timer? _processingTimer;
        private long _totalBytesUploaded = 0;
        private int _completedUploadsCount = 0;

        public UploadProcessorService(
            IServiceProvider serviceProvider,
            IHubContext<UploadStatusHub> hubContext,
            ILogger<UploadProcessorService> logger)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("Upload processor is already running");
                return;
            }

            _logger.LogInformation("Starting upload processor service...");

            await _processingLock.WaitAsync(cancellationToken);
            try
            {
                _isRunning = true;
                _isPaused = false;
                _startedAt = DateTime.UtcNow;

                // Start the processing timer
                var intervalSeconds = await GetProcessingIntervalAsync();
                _processingTimer = new Timer(ProcessUploadsCallback, null,
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(intervalSeconds));

                _logger.LogInformation("Upload processor started with {IntervalSeconds}s processing interval", intervalSeconds);

                // Notify clients
                await _hubContext.Clients.All.SendAsync("UploadProcessorStatusChanged",
                    new { IsRunning = true, StartedAt = _startedAt }, cancellationToken);
            }
            finally
            {
                _processingLock.Release();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                _logger.LogWarning("Upload processor is not running");
                return;
            }

            _logger.LogInformation("Stopping upload processor service...");

            await _processingLock.WaitAsync(cancellationToken);
            try
            {
                _processingTimer?.Dispose();
                _processingTimer = null;

                // Wait for active uploads to complete (with timeout)
                var timeout = TimeSpan.FromSeconds(30);
                var waitStart = DateTime.UtcNow;

                while (_activeUploads.Count > 0 && DateTime.UtcNow - waitStart < timeout)
                {
                    _logger.LogInformation("Waiting for {ActiveCount} uploads to complete...", _activeUploads.Count);
                    await Task.Delay(1000, cancellationToken);
                }

                if (_activeUploads.Count > 0)
                {
                    _logger.LogWarning("Stopped with {ActiveCount} uploads still in progress", _activeUploads.Count);
                }

                _isRunning = false;
                _isPaused = false;
                _activeUploads.Clear();

                _logger.LogInformation("Upload processor stopped");

                // Notify clients
                await _hubContext.Clients.All.SendAsync("UploadProcessorStatusChanged",
                    new { IsRunning = false }, cancellationToken);
            }
            finally
            {
                _processingLock.Release();
            }
        }

        public Task<bool> IsRunningAsync()
        {
            return Task.FromResult(_isRunning);
        }

        public async Task<UploadProcessorStatus> GetStatusAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var uploadService = scope.ServiceProvider.GetRequiredService<IUploadQueueService>();

            var pendingUploads = await uploadService.GetPendingUploadsAsync();
            var failedUploads = await uploadService.GetFailedUploadsAsync();

            return new UploadProcessorStatus
            {
                IsRunning = _isRunning,
                IsPaused = _isPaused,
                StartedAt = _startedAt,
                ActiveUploads = _activeUploads.Count,
                PendingCount = pendingUploads.Count(),
                FailedCount = failedUploads.Count(),
                CompletedCount = _completedUploadsCount,
                TotalBytesUploaded = _totalBytesUploaded,
                AverageUploadSpeedMbps = CalculateAverageSpeed(),
                LastUploadCompleted = GetLastCompletedTime(),
                ActiveUploadInfo = _activeUploads.Values.ToList(),
                RecentErrors = _recentErrors.Take(10).ToList()
            };
        }

        public async Task ProcessPendingUploadsAsync(int maxConcurrent = 3)
        {
            if (!_isRunning || _isPaused)
                return;

            if (_activeUploads.Count >= maxConcurrent)
            {
                _logger.LogDebug("Max concurrent uploads reached ({Count}/{Max})", _activeUploads.Count, maxConcurrent);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var uploadService = scope.ServiceProvider.GetRequiredService<IUploadQueueService>();
            var azureService = scope.ServiceProvider.GetRequiredService<IAzureStorageService>();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

            // Check Azure connection
            if (!await azureService.IsConnectedAsync())
            {
                _logger.LogWarning("Azure Storage is not connected, skipping upload processing");
                AddRecentError("Azure Storage connection failed");
                return;
            }

            // Get pending uploads
            var pendingUploads = (await uploadService.GetPendingUploadsAsync())
                .Take(maxConcurrent - _activeUploads.Count)
                .ToList();

            if (!pendingUploads.Any())
                return;

            var defaultContainer = await configService.GetValueAsync("Azure.DefaultContainer") ?? "gateway-data";

            // Process uploads concurrently
            var uploadTasks = pendingUploads.Select(upload => ProcessSingleUploadAsync(upload, defaultContainer));

            try
            {
                await Task.WhenAll(uploadTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in concurrent upload processing");
            }
        }

        public async Task RetryFailedUploadsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var uploadService = scope.ServiceProvider.GetRequiredService<IUploadQueueService>();

            await uploadService.ResetFailedUploadsAsync();
            _logger.LogInformation("Reset failed uploads for retry");
        }

        public Task PauseProcessingAsync()
        {
            _isPaused = true;
            _logger.LogInformation("Upload processing paused");
            return Task.CompletedTask;
        }

        public Task ResumeProcessingAsync()
        {
            _isPaused = false;
            _logger.LogInformation("Upload processing resumed");
            return Task.CompletedTask;
        }

        private async void ProcessUploadsCallback(object? state)
        {
            try
            {
                if (!_isRunning || _isPaused)
                    return;

                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                var maxConcurrent = await configService.GetValueAsync<int?>("Azure.MaxConcurrentUploads") ?? 3;

                await ProcessPendingUploadsAsync(maxConcurrent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in upload processing callback");
                AddRecentError($"Processing error: {ex.Message}");
            }
        }

        private async Task ProcessSingleUploadAsync(UploadQueue upload, string defaultContainer)
        {
            var activeUpload = new ActiveUploadInfo
            {
                UploadId = upload.Id,
                FileName = upload.FileName,
                TotalBytes = upload.FileSizeBytes,
                StartedAt = DateTime.UtcNow,
                Status = "Starting"
            };

            _activeUploads[upload.Id] = activeUpload;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var uploadService = scope.ServiceProvider.GetRequiredService<IUploadQueueService>();
                var azureService = scope.ServiceProvider.GetRequiredService<IAzureStorageService>();

                // Update status to processing
                await uploadService.UpdateStatusAsync(upload.Id, FileStatus.Processing);
                activeUpload.Status = "Processing";

                // Validate file still exists
                if (!File.Exists(upload.FilePath))
                {
                    var error = "File not found";
                    await uploadService.UpdateStatusAsync(upload.Id, FileStatus.Failed, error);
                    AddRecentError($"{upload.FileName}: {error}");
                    return;
                }

                // Create progress tracker
                var progressTracker = new Progress<AzureUploadProgress>(progress =>
                {
                    activeUpload.BytesUploaded = progress.BytesUploaded;
                    activeUpload.PercentComplete = progress.PercentComplete;
                    activeUpload.Status = progress.StatusMessage ?? "Uploading";

                    // Update database progress
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var progressScope = _serviceProvider.CreateScope();
                            var progressUploadService = progressScope.ServiceProvider.GetRequiredService<IUploadQueueService>();
                            await progressUploadService.UpdateProgressAsync(
                                upload.Id, progress.BytesUploaded, progress.TotalBytes, progress.StatusMessage);

                            // Notify clients via SignalR
                            await _hubContext.Clients.All.SendAsync("UploadProgressUpdated", new
                            {
                                UploadId = upload.Id,
                                FileName = upload.FileName,
                                PercentComplete = progress.PercentComplete,
                                BytesUploaded = progress.BytesUploaded,
                                TotalBytes = progress.TotalBytes
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error updating upload progress for {UploadId}", upload.Id);
                        }
                    });
                });

                // Perform the upload
                activeUpload.Status = "Uploading";
                var uploadResult = await azureService.UploadFileAsync(
                    upload.FilePath,
                    upload.AzureContainer ?? defaultContainer,
                    null,
                    progressTracker);

                if (uploadResult.IsSuccess)
                {
                    // Mark as completed
                    await uploadService.MarkAsCompletedAsync(
                        upload.Id,
                        uploadResult.BlobUrl!,
                        (long)uploadResult.Duration.TotalMilliseconds);

                    activeUpload.Status = "Completed";
                    activeUpload.PercentComplete = 100;

                    // Update statistics
                    Interlocked.Add(ref _totalBytesUploaded, uploadResult.UploadedBytes);
                    Interlocked.Increment(ref _completedUploadsCount);

                    _logger.LogInformation("Successfully uploaded {FileName} (ID: {UploadId}) to {BlobUrl}",
                        upload.FileName, upload.Id, uploadResult.BlobUrl);

                    // Notify clients
                    await _hubContext.Clients.All.SendAsync("UploadCompleted", new
                    {
                        UploadId = upload.Id,
                        FileName = upload.FileName,
                        BlobUrl = uploadResult.BlobUrl,
                        Duration = uploadResult.Duration.TotalSeconds
                    });

                    // Archive the source file
                    await ArchiveSourceFileAsync(upload.FilePath, "completed");
                }
                else
                {
                    // Handle failure
                    await uploadService.IncrementAttemptAsync(upload.Id, uploadResult.ErrorMessage);
                    activeUpload.Status = "Failed";

                    AddRecentError($"{upload.FileName}: {uploadResult.ErrorMessage}");

                    _logger.LogError("Upload failed for {FileName} (ID: {UploadId}): {Error}",
                        upload.FileName, upload.Id, uploadResult.ErrorMessage);

                    // Notify clients
                    await _hubContext.Clients.All.SendAsync("UploadFailed", new
                    {
                        UploadId = upload.Id,
                        FileName = upload.FileName,
                        Error = uploadResult.ErrorMessage,
                        AttemptCount = upload.AttemptCount + 1
                    });
                }
            }
            catch (Exception ex)
            {
                using var scope = _serviceProvider.CreateScope();
                var uploadService = scope.ServiceProvider.GetRequiredService<IUploadQueueService>();

                var error = $"Unexpected error: {ex.Message}";
                await uploadService.IncrementAttemptAsync(upload.Id, error);

                activeUpload.Status = "Error";
                AddRecentError($"{upload.FileName}: {error}");

                _logger.LogError(ex, "Unexpected error processing upload {UploadId}: {FileName}",
                    upload.Id, upload.FileName);
            }
            finally
            {
                _activeUploads.TryRemove(upload.Id, out _);
            }
        }

        private async Task ArchiveSourceFileAsync(string sourceFilePath, string reason)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

                var archivePath = await configService.GetValueAsync("Monitoring.ArchivePath") ?? "./archive";
                var reasonPath = Path.Combine(archivePath, reason);

                // Get the incoming folder path to preserve folder structure
                var incomingPath = await configService.GetValueAsync("Monitoring.FolderPath") ?? "";
                
                if (!string.IsNullOrEmpty(incomingPath))
                {
                    // Use the new overload that preserves folder structure
                    await FileHelper.MoveFileToArchiveAsync(sourceFilePath, reasonPath, incomingPath);
                }
                else
                {
                    // Fallback to the original method if no incoming path is configured
                    await FileHelper.MoveFileToArchiveAsync(sourceFilePath, reasonPath);
                }
                
                _logger.LogDebug("Archived file to {ReasonPath}: {FileName}", reasonPath, Path.GetFileName(sourceFilePath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to archive source file: {FilePath}", sourceFilePath);
                // Don't fail the upload for archiving issues
            }
        }

        private async Task<int> GetProcessingIntervalAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                return await configService.GetValueAsync<int?>("Upload.ProcessingIntervalSeconds") ?? 10;
            }
            catch
            {
                return 10; // Default fallback
            }
        }

        private double CalculateAverageSpeed()
        {
            if (_totalBytesUploaded == 0 || !_isRunning)
                return 0;

            var elapsedMinutes = (DateTime.UtcNow - _startedAt).TotalMinutes;
            if (elapsedMinutes == 0)
                return 0;

            var totalMegabytes = _totalBytesUploaded / (1024.0 * 1024.0);
            return Math.Round(totalMegabytes / elapsedMinutes, 2);
        }

        private DateTime? GetLastCompletedTime()
        {
            if (_completedUploadsCount == 0)
                return null;

            // This would ideally come from database, but for now return approximate time
            return DateTime.UtcNow.AddSeconds(-30);
        }

        private void AddRecentError(string error)
        {
            _recentErrors.Enqueue($"[{DateTime.UtcNow:HH:mm:ss}] {error}");

            // Keep only recent errors (max 50)
            while (_recentErrors.Count > 50)
            {
                _recentErrors.TryDequeue(out _);
            }
        }
    }
}