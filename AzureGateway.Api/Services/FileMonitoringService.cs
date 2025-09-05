using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Models;
using AzureGateway.Api.Utilities;
using System.Collections.Concurrent;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class FileMonitoringService : IFileMonitoringService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FileMonitoringService> _logger;
        private readonly ConcurrentDictionary<int, IFolderWatcher> _folderWatchers = new();
        private readonly ConcurrentDictionary<int, DataSourceStatus> _sourceStatuses = new();
        private readonly Timer _refreshTimer;
        private bool _isRunning = false;
        private DateTime _startedAt;
        private long _totalFilesProcessed = 0;

        public FileMonitoringService(IServiceProvider serviceProvider, ILogger<FileMonitoringService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;

            // Refresh data sources every 5 minutes
            _refreshTimer = new Timer(async _ => await RefreshDataSourcesAsync(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("File monitoring service is already running");
                return;
            }

            _logger.LogInformation("Starting file monitoring service...");
            _startedAt = DateTime.UtcNow;

            try
            {
                await RefreshDataSourcesAsync();
                _isRunning = true;
                _logger.LogInformation("File monitoring service started with {FolderWatchers} folder watchers",
                    _folderWatchers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start file monitoring service");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                _logger.LogWarning("File monitoring service is not running");
                return;
            }

            _logger.LogInformation("Stopping file monitoring service...");

            // Stop all folder watchers
            foreach (var watcher in _folderWatchers.Values)
            {
                try
                {
                    await watcher.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping folder watcher");
                }
            }

            _folderWatchers.Clear();
            _sourceStatuses.Clear();
            _isRunning = false;

            _logger.LogInformation("File monitoring service stopped");
        }

        public Task<bool> IsRunningAsync()
        {
            return Task.FromResult(_isRunning);
        }

        public FileMonitoringStatus GetStatusAsync()
        {
            return new FileMonitoringStatus
            {
                IsRunning = _isRunning,
                StartedAt = _startedAt,
                ActiveFolderWatchers = _folderWatchers.Count,
                TotalFilesProcessed = _totalFilesProcessed,
                LastFileProcessed = _sourceStatuses.Values.Max(s => s.LastActivity),
                DataSources = _sourceStatuses.Values.ToList()
            };
        }

        public async Task RefreshDataSourcesAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var dataSources = await context.DataSourceConfigs
                    .Where(ds => ds.IsEnabled)
                    .ToListAsync();

                _logger.LogDebug("Refreshing {Count} enabled data sources", dataSources.Count);

                // Update folder watchers
                await UpdateFolderWatchersAsync(dataSources.Where(ds => ds.SourceType == DataSource.Folder));

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh data sources");
            }
        }

        private async Task UpdateFolderWatchersAsync(IEnumerable<DataSourceConfig> folderSources)
        {
            var currentIds = folderSources.Select(ds => ds.Id).ToHashSet();
            var existingIds = _folderWatchers.Keys.ToHashSet();

            // Remove stopped/disabled watchers
            foreach (var id in existingIds.Except(currentIds))
            {
                if (_folderWatchers.TryRemove(id, out var watcher))
                {
                    await watcher.StopAsync();
                    _sourceStatuses.TryRemove(id, out _);
                    _logger.LogInformation("Stopped folder watcher for data source {Id}", id);
                }
            }

            // Add/update active watchers
            foreach (var source in folderSources)
            {
                // Safety check: ensure this is a Folder source before creating a watcher
                if (source.SourceType != DataSource.Folder)
                {
                    _logger.LogDebug("Skipping non-Folder data source {Id} ({Type}) in file monitoring", source.Id, source.SourceType);
                    continue;
                }

                if (!_folderWatchers.ContainsKey(source.Id))
                {
                    var watcher = new FolderWatcher(source, _serviceProvider, OnFileProcessed, OnError);
                    _folderWatchers[source.Id] = watcher;
                    _sourceStatuses[source.Id] = new DataSourceStatus
                    {
                        Id = source.Id,
                        Name = source.Name,
                        Type = source.SourceType,
                        IsEnabled = source.IsEnabled,
                        IsActive = false
                    };

                    await watcher.StartAsync();
                    _sourceStatuses[source.Id].IsActive = true;
                    _logger.LogInformation("Started folder watcher for {Name} at {Path}", source.Name, source.FolderPath);
                }
            }
        }

        private async Task OnFileProcessed(int dataSourceId, string fileName)
        {
            Interlocked.Increment(ref _totalFilesProcessed);

            if (_sourceStatuses.TryGetValue(dataSourceId, out var status))
            {
                status.LastActivity = DateTime.UtcNow;
                status.FilesProcessed++;
                status.LastError = null;
                status.LastErrorAt = null;
            }

            _logger.LogDebug("File processed from source {DataSourceId}: {FileName}", dataSourceId, fileName);
        }

        private async Task OnError(int dataSourceId, string error)
        {
            if (_sourceStatuses.TryGetValue(dataSourceId, out var status))
            {
                status.LastError = error;
                status.LastErrorAt = DateTime.UtcNow;
            }

            _logger.LogError("Error in data source {DataSourceId}: {Error}", dataSourceId, error);
        }
    }
}