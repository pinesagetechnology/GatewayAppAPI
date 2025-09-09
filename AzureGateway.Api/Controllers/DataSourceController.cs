using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AzureGateway.Api.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using AzureGateway.Api.Data;
    using AzureGateway.Api.Models;
    using AzureGateway.Api.Services.interfaces;

    [ApiController]
    [Route("api/[controller]")]
    public class DataSourceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileMonitoringService _monitoringService;
        private readonly IApiPollingService _apiPollingService;
        private readonly ILogger<DataSourceController> _logger;

        public DataSourceController(
            ApplicationDbContext context,
            IFileMonitoringService monitoringService,
            IApiPollingService apiPollingService,
            ILogger<DataSourceController> logger)
        {
            _context = context;
            _monitoringService = monitoringService;
            _apiPollingService = apiPollingService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var dataSources = await _context.DataSourceConfigs
                    .OrderBy(ds => ds.Name)
                    .ToListAsync();
                return Ok(dataSources);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve data sources");
                return StatusCode(500, new { Error = "Failed to retrieve data sources", Details = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var dataSource = await _context.DataSourceConfigs.FindAsync(id);
                if (dataSource == null)
                    return NotFound(new { Error = "Data source not found" });

                return Ok(dataSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve data source {Id}", id);
                return StatusCode(500, new { Error = "Failed to retrieve data source", Details = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDataSourceRequest request)
        {
            try
            {
                var dataSource = new DataSourceConfig
                {
                    Name = request.Name,
                    SourceType = request.SourceType,
                    IsEnabled = request.IsEnabled,
                    FolderPath = request.FolderPath,
                    ApiEndpoint = request.ApiEndpoint,
                    ApiKey = request.ApiKey,
                    PollingIntervalMinutes = request.PollingIntervalMinutes,
                    FilePattern = request.FilePattern,
                    AdditionalSettings = request.AdditionalSettings,
                    CreatedAt = DateTime.UtcNow
                };

                // Validate based on source type
                if (dataSource.SourceType == DataSource.Folder)
                {
                    if (string.IsNullOrEmpty(dataSource.FolderPath))
                        return BadRequest(new { Error = "FolderPath is required for folder data sources" });
                    
                    // Validate folder path format (but don't create it yet - let the watcher handle that)
                    try
                    {
                        var normalizedPath = Path.GetFullPath(dataSource.FolderPath);
                        dataSource.FolderPath = normalizedPath; // Store the normalized path
                    }
                    catch (Exception ex)
                    {
                        return BadRequest(new { Error = $"Invalid folder path format: {ex.Message}" });
                    }
                }
                else if (dataSource.SourceType == DataSource.Api)
                {
                    if (string.IsNullOrEmpty(dataSource.ApiEndpoint))
                        return BadRequest(new { Error = "ApiEndpoint is required for API data sources" });
                }

                _context.DataSourceConfigs.Add(dataSource);
                await _context.SaveChangesAsync();

                // Refresh monitoring service if it's running
                if (dataSource.SourceType == DataSource.Folder && await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
                }

                if (dataSource.SourceType == DataSource.Api && await _apiPollingService.IsRunningAsync())
                {
                    await _apiPollingService.RefreshDataSourcesAsync();
                }

                _logger.LogInformation("Created data source: {Name} (ID: {Id})", dataSource.Name, dataSource.Id);
                return CreatedAtAction(nameof(GetById), new { id = dataSource.Id }, dataSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create data source");
                return StatusCode(500, new { Error = "Failed to create data source", Details = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateDataSourceRequest request)
        {
            try
            {
                var dataSource = await _context.DataSourceConfigs.FindAsync(id);
                if (dataSource == null)
                    return NotFound(new { Error = "Data source not found" });

                dataSource.Name = request.Name;
                dataSource.IsEnabled = request.IsEnabled;
                
                // Validate folder path if it's a folder data source
                if (dataSource.SourceType == DataSource.Folder && !string.IsNullOrEmpty(request.FolderPath))
                {
                    try
                    {
                        var normalizedPath = Path.GetFullPath(request.FolderPath);
                        dataSource.FolderPath = normalizedPath; // Store the normalized path
                    }
                    catch (Exception ex)
                    {
                        return BadRequest(new { Error = $"Invalid folder path format: {ex.Message}" });
                    }
                }
                else
                {
                    dataSource.FolderPath = request.FolderPath;
                }
                
                dataSource.ApiEndpoint = request.ApiEndpoint;
                dataSource.ApiKey = request.ApiKey;
                dataSource.PollingIntervalMinutes = request.PollingIntervalMinutes;
                dataSource.FilePattern = request.FilePattern;
                dataSource.AdditionalSettings = request.AdditionalSettings;

                await _context.SaveChangesAsync();

                // Refresh monitoring service
                if (dataSource.SourceType == DataSource.Folder && await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
                }

                if(dataSource.SourceType == DataSource.Api && await _apiPollingService.IsRunningAsync())
                {
                    await _apiPollingService.RefreshDataSourcesAsync();
                }

                _logger.LogInformation("Updated data source: {Name} (ID: {Id})", dataSource.Name, dataSource.Id);
                return Ok(dataSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update data source {Id}", id);
                return StatusCode(500, new { Error = "Failed to update data source", Details = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var dataSource = await _context.DataSourceConfigs.FindAsync(id);
                if (dataSource == null)
                    return NotFound(new { Error = "Data source not found" });

                _context.DataSourceConfigs.Remove(dataSource);
                await _context.SaveChangesAsync();

                // Refresh monitoring service
                if (dataSource.SourceType == DataSource.Folder && await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
                }

                if (dataSource.SourceType == DataSource.Api && await _apiPollingService.IsRunningAsync())
                {
                    await _apiPollingService.RefreshDataSourcesAsync();
                }

                _logger.LogInformation("Deleted data source: {Name} (ID: {Id})", dataSource.Name, dataSource.Id);
                return Ok(new { Message = "Data source deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete data source {Id}", id);
                return StatusCode(500, new { Error = "Failed to delete data source", Details = ex.Message });
            }
        }

        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            try
            {
                var dataSource = await _context.DataSourceConfigs.FindAsync(id);
                if (dataSource == null)
                    return NotFound(new { Error = "Data source not found" });

                dataSource.IsEnabled = !dataSource.IsEnabled;
                await _context.SaveChangesAsync();

                // Refresh monitoring service
                if (dataSource.SourceType == DataSource.Folder && await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
                }

                if (dataSource.SourceType == DataSource.Api && await _apiPollingService.IsRunningAsync())
                {
                    await _apiPollingService.RefreshDataSourcesAsync();
                }

                var status = dataSource.IsEnabled ? "enabled" : "disabled";
                _logger.LogInformation("Data source {Name} (ID: {Id}) {Status}", dataSource.Name, dataSource.Id, status);

                return Ok(new { Message = $"Data source {status} successfully", IsEnabled = dataSource.IsEnabled });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle data source {Id}", id);
                return StatusCode(500, new { Error = "Failed to toggle data source", Details = ex.Message });
            }
        }

        
    }
}
