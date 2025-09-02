// Controllers/FileMonitoringController.cs
using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Models;
using AzureGateway.Api.Data;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileMonitoringController : ControllerBase
    {
        private readonly IFileMonitoringService _monitoringService;
        private readonly ILogger<FileMonitoringController> _logger;

        public FileMonitoringController(
            IFileMonitoringService monitoringService,
            ILogger<FileMonitoringController> logger)
        {
            _monitoringService = monitoringService;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = await _monitoringService.GetStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get monitoring status");
                return StatusCode(500, new { Error = "Failed to get status", Details = ex.Message });
            }
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            try
            {
                await _monitoringService.StartAsync(CancellationToken.None);
                return Ok(new { Message = "File monitoring started successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start file monitoring");
                return StatusCode(500, new { Error = "Failed to start monitoring", Details = ex.Message });
            }
        }

        [HttpPost("stop")]
        public async Task<IActionResult> Stop()
        {
            try
            {
                await _monitoringService.StopAsync(CancellationToken.None);
                return Ok(new { Message = "File monitoring stopped successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop file monitoring");
                return StatusCode(500, new { Error = "Failed to stop monitoring", Details = ex.Message });
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshDataSources()
        {
            try
            {
                await _monitoringService.RefreshDataSourcesAsync();
                return Ok(new { Message = "Data sources refreshed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh data sources");
                return StatusCode(500, new { Error = "Failed to refresh data sources", Details = ex.Message });
            }
        }
    }

    public class CreateDataSourceRequest
    {
        public string Name { get; set; } = string.Empty;
        public DataSource SourceType { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? FolderPath { get; set; }
        public string? ApiEndpoint { get; set; }
        public string? ApiKey { get; set; }
        public int PollingIntervalMinutes { get; set; } = 5;
        public string? FilePattern { get; set; }
        public string? AdditionalSettings { get; set; }
    }

    public class UpdateDataSourceRequest
    {
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string? FolderPath { get; set; }
        public string? ApiEndpoint { get; set; }
        public string? ApiKey { get; set; }
        public int PollingIntervalMinutes { get; set; }
        public string? FilePattern { get; set; }
        public string? AdditionalSettings { get; set; }
    }
}