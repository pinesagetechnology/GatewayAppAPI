using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Services.interfaces;
using AzureGateway.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly IAzureStorageService _azureStorageService;
        private readonly IFileMonitoringService _fileMonitoringService;
        private readonly ApplicationDbContext _dbContext;
        private readonly IDatabaseHealthService _databaseHealthService;
        private readonly ILogger<HealthController> _logger;

        public HealthController(
            IAzureStorageService azureStorageService,
            IFileMonitoringService fileMonitoringService,
            ApplicationDbContext dbContext,
            IDatabaseHealthService databaseHealthService,
            ILogger<HealthController> logger)
        {
            _azureStorageService = azureStorageService;
            _fileMonitoringService = fileMonitoringService;
            _dbContext = dbContext;
            _databaseHealthService = databaseHealthService;
            _logger = logger;
            _logger.LogDebug("HealthController initialized");
        }

        [HttpGet]
        public async Task<IActionResult> GetHealth()
        {
            _logger.LogInformation("Health check requested from {IPAddress}", 
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
            
            var startTime = DateTime.UtcNow;
            try
            {
                _logger.LogDebug("Starting comprehensive health check...");
                
                var db = await CheckDatabaseHealthAsync();
                var storage = await CheckAzureStorageHealthAsync();
                var monitoring = CheckFileMonitoringHealthAsync();

                // Compute UI-compatible flags
                var isHealthy =
                    (db as dynamic).CanConnect == true &&
                    (storage as dynamic).IsConnected == true &&
                    (monitoring as dynamic).IsRunning == true;

                var issues = new List<string>();
                if ((db as dynamic).CanConnect != true) issues.Add("Database cannot connect");
                if ((storage as dynamic).IsConnected != true) issues.Add("Azure Storage not connected");
                if ((monitoring as dynamic).IsRunning != true) issues.Add("File monitoring is not running");

                var healthStatus = new
                {
                    Status = isHealthy ? "Healthy" : "Issues",
                    Timestamp = startTime,
                    Database = db,
                    AzureStorage = storage,
                    FileMonitoring = monitoring,
                    // UI-compat fields
                    IsHealthy = isHealthy,
                    Issues = issues,
                    CheckedAt = startTime
                };

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation("Health check completed successfully in {Duration}ms", duration.TotalMilliseconds);
                
                return Ok(healthStatus);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "Health check failed after {Duration}ms", duration.TotalMilliseconds);
                return StatusCode(500, new { Status = "Unhealthy", Error = ex.Message, IsHealthy = false, Issues = new [] { ex.Message }, CheckedAt = DateTime.UtcNow });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetSystemStats()
        {
            _logger.LogInformation("System stats requested from {IPAddress}", 
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
            
            var startTime = DateTime.UtcNow;
            try
            {
                _logger.LogDebug("Starting system stats collection...");
                
                // Return flat stats expected by UI from DatabaseHealthService
                var stats = await _databaseHealthService.GetDatabaseStatsAsync();
                stats["LastCalculated"] = DateTime.UtcNow;

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation("System stats collected successfully in {Duration}ms", duration.TotalMilliseconds);
                
                return Ok(stats);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "Failed to get system stats after {Duration}ms", duration.TotalMilliseconds);
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        private async Task<object> CheckDatabaseHealthAsync()
        {
            _logger.LogDebug("Checking database health...");
            try
            {
                var canConnect = await _dbContext.Database.CanConnectAsync();
                var provider = _dbContext.Database.ProviderName;
                
                _logger.LogDebug("Database health check result: Connected={Connected}, Provider={Provider}", 
                    canConnect, provider);
                
                return new
                {
                    Status = canConnect ? "Connected" : "Disconnected",
                    CanConnect = canConnect,
                    Provider = provider
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return new
                {
                    Status = "Error",
                    CanConnect = false,
                    Error = ex.Message
                };
            }
        }

        private async Task<object> CheckAzureStorageHealthAsync()
        {
            _logger.LogDebug("Checking Azure Storage health...");
            try
            {
                var isConnected = await _azureStorageService.IsConnectedAsync();
                
                _logger.LogDebug("Azure Storage health check result: Connected={Connected}", isConnected);
                
                return new
                {
                    Status = isConnected ? "Connected" : "Disconnected",
                    IsConnected = isConnected
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure Storage health check failed");
                return new
                {
                    Status = "Error",
                    IsConnected = false,
                    Error = ex.Message
                };
            }
        }

        private object CheckFileMonitoringHealthAsync()
        {
            _logger.LogDebug("Checking file monitoring health...");
            try
            {
                var status = _fileMonitoringService.GetStatusAsync();
                
                _logger.LogDebug("File monitoring health check result: Running={Running}, FilesProcessed={FilesProcessed}", 
                    status.IsRunning, status.TotalFilesProcessed);
                
                return new
                {
                    Status = status.IsRunning ? "Running" : "Stopped",
                    IsRunning = status.IsRunning,
                    StartedAt = status.StartedAt,
                    TotalFilesProcessed = status.TotalFilesProcessed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File monitoring health check failed");
                return new
                {
                    Status = "Error",
                    IsRunning = false,
                    Error = ex.Message
                };
            }
        }

        private async Task<object> GetDatabaseStatsAsync()
        {
            _logger.LogDebug("Collecting database statistics...");
            try
            {
                var uploadQueueCount = await _dbContext.UploadQueue.CountAsync();
                var uploadHistoryCount = await _dbContext.UploadHistory.CountAsync();
                var configurationCount = await _dbContext.Configuration.CountAsync();

                _logger.LogDebug("Database stats: Queue={Queue}, History={History}, Config={Config}", 
                    uploadQueueCount, uploadHistoryCount, configurationCount);

                return new
                {
                    UploadQueueCount = uploadQueueCount,
                    UploadHistoryCount = uploadHistoryCount,
                    ConfigurationCount = configurationCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get database stats");
                return new { Error = ex.Message };
            }
        }

        private async Task<object> GetUploadQueueStatsAsync()
        {
            _logger.LogDebug("Collecting upload queue statistics...");
            try
            {
                var pendingCount = await _dbContext.UploadQueue.CountAsync(u => u.Status == FileStatus.Pending);
                var processingCount = await _dbContext.UploadQueue.CountAsync(u => u.Status == FileStatus.Processing);
                var completedCount = await _dbContext.UploadQueue.CountAsync(u => u.Status == FileStatus.Completed);
                var failedCount = await _dbContext.UploadQueue.CountAsync(u => u.Status == FileStatus.Failed);

                var total = pendingCount + processingCount + completedCount + failedCount;
                
                _logger.LogDebug("Upload queue stats: Pending={Pending}, Processing={Processing}, Completed={Completed}, Failed={Failed}, Total={Total}", 
                    pendingCount, processingCount, completedCount, failedCount, total);

                return new
                {
                    Pending = pendingCount,
                    Processing = processingCount,
                    Completed = completedCount,
                    Failed = failedCount,
                    Total = total
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get upload queue stats");
                return new { Error = ex.Message };
            }
        }

        private object GetSystemInfo()
        {
            _logger.LogDebug("Collecting system information...");
            
            var systemInfo = new
            {
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                Framework = Environment.Version.ToString(),
                OS = Environment.OSVersion.ToString(),
                MachineName = Environment.MachineName,
                ProcessorCount = Environment.ProcessorCount,
                WorkingSet = Environment.WorkingSet,
                StartTime = Process.GetCurrentProcess().StartTime
            };

            _logger.LogDebug("System info: Environment={Environment}, Framework={Framework}, OS={OS}, Machine={Machine}", 
                systemInfo.Environment, systemInfo.Framework, systemInfo.OS, systemInfo.MachineName);

            return systemInfo;
        }
    }
}