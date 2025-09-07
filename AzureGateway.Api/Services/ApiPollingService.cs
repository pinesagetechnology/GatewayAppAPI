using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Models;
using System.Collections.Concurrent;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class ApiPollingService : IApiPollingService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ApiPollingService> _logger;
        private readonly ConcurrentDictionary<int, IApiPoller> _pollers = new();
        private readonly ConcurrentDictionary<int, DataSourceStatus> _sourceStatuses = new();
        private readonly Timer _refreshTimer;
        private bool _isRunning = false;
        private DateTime _startedAt;
        private long _totalItemsProcessed = 0;

        public ApiPollingService(IServiceProvider serviceProvider, ILogger<ApiPollingService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _refreshTimer = new Timer(async _ => await RefreshDataSourcesAsync(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("API polling service is already running");
                return;
            }

            _logger.LogInformation("Starting API polling service...");
            _startedAt = DateTime.UtcNow;

            await RefreshDataSourcesAsync();
            _isRunning = true;
            _logger.LogInformation("API polling service started with {ApiPollers} pollers", _pollers.Count);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                _logger.LogWarning("API polling service is not running");
                return;
            }

            _logger.LogInformation("Stopping API polling service...");

            foreach (var poller in _pollers.Values)
            {
                try { await poller.StopAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "Error stopping API poller"); }
            }

            _pollers.Clear();
            _sourceStatuses.Clear();
            _isRunning = false;
            _logger.LogInformation("API polling service stopped");
        }

        public Task<bool> IsRunningAsync() => Task.FromResult(_isRunning);

        public async Task<ApiPollingStatus> GetStatusAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Get all API data sources from database
                var apiSources = await context.DataSourceConfigs
                    .Where(ds => ds.SourceType == DataSource.Api)
                    .ToListAsync();

                // Create status list from database
                var dataSourceStatuses = new List<DataSourceStatus>();
                
                foreach (var source in apiSources)
                {
                    var status = new DataSourceStatus
                    {
                        Id = source.Id,
                        Name = source.Name,
                        Type = source.SourceType,
                        IsEnabled = source.IsEnabled,
                        IsActive = _pollers.ContainsKey(source.Id), // Active if currently polling
                        LastActivity = source.LastProcessedAt,
                        FilesProcessed = 0, // Could be tracked separately if needed
                        LastError = null,
                        LastErrorAt = null
                    };

                    // Update with in-memory status if available (for runtime stats)
                    if (_sourceStatuses.TryGetValue(source.Id, out var inMemoryStatus))
                    {
                        status.LastActivity = inMemoryStatus.LastActivity ?? source.LastProcessedAt;
                        status.FilesProcessed = inMemoryStatus.FilesProcessed;
                        status.LastError = inMemoryStatus.LastError;
                        status.LastErrorAt = inMemoryStatus.LastErrorAt;
                    }

                    dataSourceStatuses.Add(status);
                }

                return new ApiPollingStatus
                {
                    IsRunning = _isRunning,
                    StartedAt = _startedAt,
                    ActiveApiPollers = _pollers.Count,
                    TotalItemsProcessed = _totalItemsProcessed,
                    LastActivity = dataSourceStatuses.Any() ? dataSourceStatuses.Max(s => s.LastActivity) : null,
                    DataSources = dataSourceStatuses
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get API polling status from database");
                // Fallback to basic status
                return new ApiPollingStatus
                {
                    IsRunning = _isRunning,
                    StartedAt = _startedAt,
                    ActiveApiPollers = _pollers.Count,
                    TotalItemsProcessed = _totalItemsProcessed,
                    LastActivity = null,
                    DataSources = new List<DataSourceStatus>()
                };
            }
        }

        public async Task StartDataSourceAsync(int dataSourceId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var dataSource = await context.DataSourceConfigs
                    .FirstOrDefaultAsync(ds => ds.Id == dataSourceId && ds.SourceType == DataSource.Api);

                if (dataSource == null)
                {
                    _logger.LogWarning("Data source {DataSourceId} not found or not an API source", dataSourceId);
                    return;
                }

                // Update database to enable the data source
                dataSource.IsEnabled = true;
                await context.SaveChangesAsync();

                // Start the poller if service is running
                if (_isRunning)
                {
                    await UpdateApiPollersAsync(new[] { dataSource });
                }

                _logger.LogInformation("Started API polling for data source {DataSourceId}: {Name}", dataSourceId, dataSource.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start API polling for data source {DataSourceId}", dataSourceId);
                throw;
            }
        }

        public async Task StopDataSourceAsync(int dataSourceId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var dataSource = await context.DataSourceConfigs
                    .FirstOrDefaultAsync(ds => ds.Id == dataSourceId && ds.SourceType == DataSource.Api);

                if (dataSource == null)
                {
                    _logger.LogWarning("Data source {DataSourceId} not found or not an API source", dataSourceId);
                    return;
                }

                // Update database to disable the data source
                dataSource.IsEnabled = false;
                await context.SaveChangesAsync();

                // Stop the poller if it's running
                if (_pollers.TryRemove(dataSourceId, out var poller))
                {
                    await poller.StopAsync();
                    _sourceStatuses.TryRemove(dataSourceId, out _);
                    _logger.LogInformation("Stopped API poller for data source {DataSourceId}: {Name}", dataSourceId, dataSource.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop API polling for data source {DataSourceId}", dataSourceId);
                throw;
            }
        }

        public async Task RefreshDataSourcesAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var apiSources = await context.DataSourceConfigs
                    .Where(ds => ds.IsEnabled && ds.SourceType == DataSource.Api)
                    .ToListAsync();

                await UpdateApiPollersAsync(apiSources);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh API data sources");
            }
        }

        private async Task UpdateApiPollersAsync(IEnumerable<DataSourceConfig> apiSources)
        {
            var currentIds = apiSources.Select(ds => ds.Id).ToHashSet();
            var existingIds = _pollers.Keys.ToHashSet();

            // Remove stopped/disabled pollers
            foreach (var id in existingIds.Except(currentIds))
            {
                if (_pollers.TryRemove(id, out var poller))
                {
                    await poller.StopAsync();
                    _sourceStatuses.TryRemove(id, out _);
                    _logger.LogInformation("Stopped API poller for data source {Id}", id);
                }
            }

            // Add/update active pollers
            foreach (var source in apiSources)
            {
                // Safety check: ensure this is an API source before creating a poller
                if (source.SourceType != DataSource.Api)
                {
                    _logger.LogDebug("Skipping non-API data source {Id} ({Type}) in API polling", source.Id, source.SourceType);
                    continue;
                }

                if (!_pollers.ContainsKey(source.Id))
                {
                    var poller = new ApiPoller(source, _serviceProvider, OnItemProcessed, OnError);
                    _pollers[source.Id] = poller;
                    _sourceStatuses[source.Id] = new DataSourceStatus
                    {
                        Id = source.Id,
                        Name = source.Name,
                        Type = source.SourceType,
                        IsEnabled = source.IsEnabled,
                        IsActive = false
                    };

                    await poller.StartAsync();
                    _sourceStatuses[source.Id].IsActive = true;
                    _logger.LogInformation("Started API poller for {Name} at {Endpoint}", source.Name, source.ApiEndpoint);
                }
            }
        }

        private Task OnItemProcessed(int dataSourceId, string fileName)
        {
            Interlocked.Increment(ref _totalItemsProcessed);
            if (_sourceStatuses.TryGetValue(dataSourceId, out var status))
            {
                status.LastActivity = DateTime.UtcNow;
                status.FilesProcessed++;
                status.LastError = null;
                status.LastErrorAt = null;
            }
            _logger.LogDebug("API item processed from source {DataSourceId}: {FileName}", dataSourceId, fileName);
            return Task.CompletedTask;
        }

        private Task OnError(int dataSourceId, string error)
        {
            if (_sourceStatuses.TryGetValue(dataSourceId, out var status))
            {
                status.LastError = error;
                status.LastErrorAt = DateTime.UtcNow;
            }
            _logger.LogError("Error in API data source {DataSourceId}: {Error}", dataSourceId, error);
            return Task.CompletedTask;
        }
    }
}

