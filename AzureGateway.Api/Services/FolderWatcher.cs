using AzureGateway.Api.Models;
using AzureGateway.Api.Services.interfaces;
using AzureGateway.Api.Utilities;
using System.Collections.Concurrent;

namespace AzureGateway.Api.Services
{
    public class FolderWatcher : IFolderWatcher, IDisposable
    {
        private readonly DataSourceConfig _config;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<int, string, Task> _onFileProcessed;
        private readonly Func<int, string, Task> _onError;
        private readonly ILogger<FolderWatcher> _logger;
        private readonly ConcurrentDictionary<string, DateTime> _processingFiles = new();

        private FileSystemWatcher? _watcher;
        private bool _isRunning = false;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public bool IsRunning => _isRunning;

        public FolderWatcher(
            DataSourceConfig config,
            IServiceProvider serviceProvider,
            Func<int, string, Task> onFileProcessed,
            Func<int, string, Task> onError)
        {
            _config = config;
            _serviceProvider = serviceProvider;
            _onFileProcessed = onFileProcessed;
            _onError = onError;
            _logger = serviceProvider.GetRequiredService<ILogger<FolderWatcher>>();
        }

        public async Task StartAsync()
        {
            if (_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (_isRunning) return;

                var folderPath = _config.FolderPath;
                if (string.IsNullOrEmpty(folderPath))
                {
                    var error = "Folder path is not configured";
                    await _onError(_config.Id, error);
                    throw new ArgumentException(error);
                }

                try
                {
                    // Use the utility method to normalize and create the folder path
                    folderPath = FileHelper.NormalizeFolderPath(folderPath, createIfNotExists: true);
                    _logger.LogInformation("Folder path validated and ready: {Path}", folderPath);
                }
                catch (Exception ex)
                {
                    var error = $"Failed to validate folder path: {folderPath}. Error: {ex.Message}";
                    _logger.LogError(ex, "Failed to validate folder path: {Path}", folderPath);
                    await _onError(_config.Id, error);
                    throw;
                }

                _watcher = new FileSystemWatcher(folderPath)
                {
                    Filter = _config.FilePattern ?? "*.*",
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = false
                };

                _watcher.Created += OnFileCreated;
                _watcher.Changed += OnFileChanged;
                _watcher.Error += OnError;

                _watcher.EnableRaisingEvents = true;
                _isRunning = true;

                _logger.LogInformation("Started folder watcher for {Name} monitoring {Path} with pattern {Pattern}",
                    _config.Name, folderPath, _config.FilePattern);

                // Process existing files on startup
                await ProcessExistingFilesAsync(folderPath);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (!_isRunning) return;

                _watcher?.Dispose();
                _watcher = null;
                _isRunning = false;
                _processingFiles.Clear();

                _logger.LogInformation("Stopped folder watcher for {Name}", _config.Name);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            await ProcessFileAsync(e.FullPath, "Created");
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            await ProcessFileAsync(e.FullPath, "Changed");
        }

        private async void OnError(object sender, ErrorEventArgs e)
        {
            var error = $"FileSystemWatcher error: {e.GetException().Message}";
            await _onError(_config.Id, error);
            _logger.LogError(e.GetException(), "FileSystemWatcher error in {Name}", _config.Name);
        }

        private async Task ProcessFileAsync(string filePath, string eventType)
        {
            try
            {
                // Prevent duplicate processing
                var fileName = Path.GetFileName(filePath);
                var now = DateTime.UtcNow;

                if (_processingFiles.TryGetValue(filePath, out var lastProcessed))
                {
                    if (now - lastProcessed < TimeSpan.FromSeconds(2))
                    {
                        return; // Skip if processed recently
                    }
                }

                _processingFiles[filePath] = now;

                // Wait for file to be completely written
                await WaitForFileToBeReady(filePath);

                // Validate file
                if (!await IsValidFileAsync(filePath))
                {
                    _logger.LogDebug("Skipping invalid file: {FilePath}", filePath);
                    return;
                }

                // Calculate hash for duplicate detection
                var hash = await FileHelper.CalculateFileHashAsync(filePath);
                var fileInfo = new FileInfo(filePath);
                var fileType = FileHelper.GetFileType(fileName);

                // Add to upload queue
                using var scope = _serviceProvider.CreateScope();
                var uploadService = scope.ServiceProvider.GetRequiredService<IUploadQueueService>();

                // Check for duplicates
                if (await uploadService.IsDuplicateAsync(hash))
                {
                    _logger.LogInformation("Duplicate file detected, skipping: {FileName}", fileName);
                    await MoveToArchiveAsync(filePath, "duplicate");
                    return;
                }

                var upload = await uploadService.AddToQueueAsync(
                    filePath, fileType, DataSource.Folder, fileInfo.Length, hash);

                _logger.LogInformation("Added file to upload queue: {FileName} (ID: {UploadId})", fileName, upload.Id);

                // Note: Files are moved to archive by UploadProcessor after successful upload
                // await MoveToArchiveAsync(filePath, "processed");

                await _onFileProcessed(_config.Id, fileName);
            }
            catch (Exception ex)
            {
                var error = $"Error processing file {filePath}: {ex.Message}";
                await _onError(_config.Id, error);
                _logger.LogError(ex, "Error processing file {FilePath}", filePath);
                
                // Archive failed files to prevent reprocessing
                await MoveToArchiveAsync(filePath, "failed");
            }
            finally
            {
                // Clean up old entries to prevent memory growth
                var cutoff = DateTime.UtcNow.AddMinutes(-5);
                var oldEntries = _processingFiles.Where(kvp => kvp.Value < cutoff).ToList();
                foreach (var entry in oldEntries)
                {
                    _processingFiles.TryRemove(entry.Key, out _);
                }
            }
        }

        private async Task ProcessExistingFilesAsync(string folderPath)
        {
            try
            {
                var pattern = _config.FilePattern ?? "*.*";
                var files = Directory.GetFiles(folderPath, pattern);

                _logger.LogInformation("Processing {Count} existing files in {Path}", files.Length, folderPath);

                foreach (var file in files)
                {
                    await ProcessFileAsync(file, "Existing");

                    // Small delay to prevent overwhelming the system
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                var error = $"Error processing existing files: {ex.Message}";
                await _onError(_config.Id, error);
                _logger.LogError(ex, "Error processing existing files in {FolderPath}", folderPath);
            }
        }

        private async Task WaitForFileToBeReady(string filePath, int maxWaitSeconds = 10)
        {
            var attempts = 0;
            var maxAttempts = maxWaitSeconds * 2; // Check every 500ms

            while (attempts < maxAttempts)
            {
                try
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    return; // File is ready
                }
                catch (IOException)
                {
                    attempts++;
                    await Task.Delay(500);
                }
                catch (UnauthorizedAccessException)
                {
                    // File might still be being written
                    attempts++;
                    await Task.Delay(500);
                }
            }

            _logger.LogWarning("File may still be in use after waiting {Seconds}s: {FilePath}", maxWaitSeconds, filePath);
        }

        private async Task<bool> IsValidFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);

                // Check if file exists and has content
                if (!fileInfo.Exists || fileInfo.Length == 0)
                    return false;

                // Check file size limits
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                var maxSizeMB = await configService.GetValueAsync<int?>("Upload.MaxFileSizeMB") ?? 100;

                if (fileInfo.Length > maxSizeMB * 1024 * 1024)
                {
                    _logger.LogWarning("File too large ({SizeMB}MB > {MaxMB}MB): {FilePath}",
                        fileInfo.Length / 1024 / 1024, maxSizeMB, filePath);
                    return false;
                }

                // Validate based on file type
                var fileType = FileHelper.GetFileType(filePath);

                if (fileType == FileType.Json)
                {
                    return await FileHelper.IsValidJsonFileAsync(filePath);
                }
                else if (fileType == FileType.Image)
                {
                    return FileHelper.IsValidImageFile(filePath);
                }

                return true; // Other file types are accepted
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating file {FilePath}", filePath);
                return false;
            }
        }

        private async Task MoveToArchiveAsync(string sourceFile, string reason)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                var archivePath = await configService.GetValueAsync("Monitoring.ArchivePath") ??
                    Path.Combine(Path.GetDirectoryName(_config.FolderPath) ?? "", "archive");

                var reasonFolder = Path.Combine(archivePath, reason);
                await FileHelper.MoveFileToArchiveAsync(sourceFile, reasonFolder);

                _logger.LogDebug("Moved file to archive ({Reason}): {FileName}", reason, Path.GetFileName(sourceFile));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive file {FilePath}", sourceFile);
                // Don't throw - file processing was successful even if archiving failed
            }
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _watcher?.Dispose();
            _semaphore?.Dispose();
        }
    }
}