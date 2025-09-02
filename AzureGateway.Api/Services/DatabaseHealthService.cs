using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Models;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class DatabaseHealthService : IDatabaseHealthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUploadQueueService _uploadQueueService;
        private readonly IConfigurationService _configService;
        private readonly ILogger<DatabaseHealthService> _logger;

        public DatabaseHealthService(
            ApplicationDbContext context,
            IUploadQueueService uploadQueueService,
            IConfigurationService configService,
            ILogger<DatabaseHealthService> logger)
        {
            _context = context;
            _uploadQueueService = uploadQueueService;
            _configService = configService;
            _logger = logger;
        }

        public async Task<DatabaseHealthStatus> CheckHealthAsync()
        {
            var health = new DatabaseHealthStatus();
            var issues = new List<string>();

            try
            {
                // Test connection
                var canConnect = await CanConnectAsync();
                if (!canConnect)
                {
                    issues.Add("Cannot connect to database");
                }

                // Get stats
                var stats = await GetDatabaseStatsAsync();
                health.Stats = stats;

                // Check for potential issues
                var pendingCount = (int)(stats.GetValueOrDefault("PendingUploads", 0));
                var failedCount = (int)(stats.GetValueOrDefault("FailedUploads", 0));

                if (pendingCount > 1000)
                {
                    issues.Add($"High number of pending uploads: {pendingCount}");
                }

                if (failedCount > 100)
                {
                    issues.Add($"High number of failed uploads: {failedCount}");
                }

                // Check database file size (if SQLite)
                await CheckDatabaseSizeAsync(issues);

                health.IsHealthy = issues.Count == 0;
                health.Status = health.IsHealthy ? "Healthy" : "Issues Detected";
                health.Issues = issues;

                _logger.LogInformation("Database health check completed. Status: {Status}", health.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                health.IsHealthy = false;
                health.Status = "Health Check Failed";
                health.Issues.Add($"Health check error: {ex.Message}");
            }

            return health;
        }

        public async Task<bool> CanConnectAsync()
        {
            try
            {
                return await _context.Database.CanConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                return false;
            }
        }

        public async Task<Dictionary<string, object>> GetDatabaseStatsAsync()
        {
            var stats = new Dictionary<string, object>();

            try
            {
                stats["PendingUploads"] = await _context.UploadQueue.CountAsync(u => u.Status == FileStatus.Pending);
                stats["ProcessingUploads"] = await _context.UploadQueue.CountAsync(u => u.Status == FileStatus.Processing);
                stats["CompletedUploads"] = await _context.UploadQueue.CountAsync(u => u.Status == FileStatus.Completed);
                stats["FailedUploads"] = await _context.UploadQueue.CountAsync(u => u.Status == FileStatus.Failed);
                stats["TotalUploads"] = await _context.UploadQueue.CountAsync();

                stats["HistoryRecords"] = await _context.UploadHistory.CountAsync();
                stats["ConfigurationEntries"] = await _context.Configuration.CountAsync();
                stats["DataSources"] = await _context.DataSourceConfigs.CountAsync();
                stats["EnabledDataSources"] = await _context.DataSourceConfigs.CountAsync(d => d.IsEnabled);

                // Recent activity
                var last24Hours = DateTime.UtcNow.AddHours(-24);
                stats["UploadsLast24Hours"] = await _context.UploadQueue.CountAsync(u => u.CreatedAt >= last24Hours);
                stats["FailuresLast24Hours"] = await _context.UploadQueue.CountAsync(u => 
                    u.Status == FileStatus.Failed && u.LastAttemptAt >= last24Hours);

                // Average file sizes
                var avgSizeBytes = await _context.UploadQueue
                    .Where(u => u.FileSizeBytes > 0)
                    .AverageAsync(u => (double)u.FileSizeBytes);
                stats["AverageFileSizeKB"] = Math.Round(avgSizeBytes / 1024, 2);

                // Database size (for SQLite)
                var dbPath = GetDatabasePath();
                if (File.Exists(dbPath))
                {
                    var dbSize = new FileInfo(dbPath).Length;
                    stats["DatabaseSizeMB"] = Math.Round(dbSize / 1024.0 / 1024.0, 2);
                }

                stats["LastCalculated"] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate database stats");
                stats["Error"] = ex.Message;
            }

            return stats;
        }

        public async Task CleanupOldDataAsync()
        {
            try
            {
                var cleanupDays = await _configService.GetValueAsync<int?>("System.CleanupDays") ?? 30;
                
                // Archive old completed uploads
                await _uploadQueueService.ArchiveCompletedUploadsAsync(cleanupDays);

                // Clean old logs
                var logCutoff = DateTime.UtcNow.AddDays(-cleanupDays);
                var oldLogs = await _context.SystemLogs
                    .Where(l => l.Timestamp < logCutoff)
                    .ToListAsync();

                if (oldLogs.Any())
                {
                    _context.SystemLogs.RemoveRange(oldLogs);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Cleaned up {Count} old log entries", oldLogs.Count);
                }

                // Clean old progress records for completed uploads
                var oldProgress = await _context.UploadProgress
                    .Include(p => p.UploadQueue)
                    .Where(p => p.UploadQueue.Status == FileStatus.Archived)
                    .ToListAsync();

                if (oldProgress.Any())
                {
                    _context.UploadProgress.RemoveRange(oldProgress);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Cleaned up {Count} old progress records", oldProgress.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database cleanup failed");
                throw;
            }
        }

        public async Task<bool> TestAllOperationsAsync()
        {
            try
            {
                // Test 1: Configuration operations
                var testKey = $"Test.{Guid.NewGuid()}";
                await _configService.SetValueAsync(testKey, "test-value", "Test configuration");
                var retrievedValue = await _configService.GetValueAsync(testKey);
                
                if (retrievedValue != "test-value")
                {
                    _logger.LogError("Configuration test failed: value mismatch");
                    return false;
                }

                // Test 2: Upload queue operations
                var testUpload = await _uploadQueueService.AddToQueueAsync(
                    "/test/path/test.json",
                    FileType.Json,
                    DataSource.Folder,
                    1024
                );

                await _uploadQueueService.UpdateStatusAsync(testUpload.Id, FileStatus.Processing);
                await _uploadQueueService.UpdateProgressAsync(testUpload.Id, 512, 1024, "Testing progress");
                await _uploadQueueService.MarkAsCompletedAsync(testUpload.Id, "https://test.blob.url", 1000);

                // Test 3: Query operations
                var recentUploads = await _uploadQueueService.GetRecentUploadsAsync(5);
                
                // Cleanup test data
                await _configService.DeleteAsync(testKey);

                _logger.LogInformation("All database operations test passed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database operations test failed");
                return false;
            }
        }

        private async Task CheckDatabaseSizeAsync(List<string> issues)
        {
            try
            {
                var dbPath = GetDatabasePath();
                if (File.Exists(dbPath))
                {
                    var dbSize = new FileInfo(dbPath).Length;
                    var dbSizeMB = dbSize / 1024.0 / 1024.0;

                    if (dbSizeMB > 500) // Warn if database is over 500MB
                    {
                        issues.Add($"Database size is large: {dbSizeMB:F2} MB");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check database file size");
            }
        }

        private string GetDatabasePath()
        {
            // Extract path from connection string
            var connectionString = _context.Database.GetConnectionString();
            if (connectionString?.Contains("Data Source=") == true)
            {
                var start = connectionString.IndexOf("Data Source=") + "Data Source=".Length;
                var end = connectionString.IndexOf(';', start);
                if (end == -1) end = connectionString.Length;
                return connectionString.Substring(start, end - start).Trim();
            }
            return "./data/gateway.db"; // fallback
        }
    }
}