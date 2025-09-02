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
        private readonly ILogger<DataSourceController> _logger;

        public DataSourceController(
            ApplicationDbContext context,
            IFileMonitoringService monitoringService,
            ILogger<DataSourceController> logger)
        {
            _context = context;
            _monitoringService = monitoringService;
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
                }
                else if (dataSource.SourceType == DataSource.Api)
                {
                    if (string.IsNullOrEmpty(dataSource.ApiEndpoint))
                        return BadRequest(new { Error = "ApiEndpoint is required for API data sources" });
                }

                _context.DataSourceConfigs.Add(dataSource);
                await _context.SaveChangesAsync();

                // Refresh monitoring service if it's running
                if (await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
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
                dataSource.FolderPath = request.FolderPath;
                dataSource.ApiEndpoint = request.ApiEndpoint;
                dataSource.ApiKey = request.ApiKey;
                dataSource.PollingIntervalMinutes = request.PollingIntervalMinutes;
                dataSource.FilePattern = request.FilePattern;
                dataSource.AdditionalSettings = request.AdditionalSettings;

                await _context.SaveChangesAsync();

                // Refresh monitoring service
                if (await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
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
                if (await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
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
                if (await _monitoringService.IsRunningAsync())
                {
                    await _monitoringService.RefreshDataSourcesAsync();
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

        [HttpPost("{id}/test")]
        public async Task<IActionResult> TestDataSource(int id)
        {
            try
            {
                var dataSource = await _context.DataSourceConfigs.FindAsync(id);
                if (dataSource == null)
                    return NotFound(new { Error = "Data source not found" });

                var testResult = await TestDataSourceAsync(dataSource);
                return Ok(testResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test data source {Id}", id);
                return StatusCode(500, new { Error = "Failed to test data source", Details = ex.Message });
            }
        }

        private async Task<object> TestDataSourceAsync(DataSourceConfig dataSource)
        {
            var result = new
            {
                DataSourceId = dataSource.Id,
                Name = dataSource.Name,
                Type = dataSource.SourceType.ToString(),
                IsSuccessful = false,
                Message = "",
                Details = new Dictionary<string, object>()
            };

            try
            {
                if (dataSource.SourceType == DataSource.Folder)
                {
                    return await TestFolderSourceAsync(dataSource);
                }
                else if (dataSource.SourceType == DataSource.Api)
                {
                    return await TestApiSourceAsync(dataSource);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing data source {Name}", dataSource.Name);
                return new
                {
                    result.DataSourceId,
                    result.Name,
                    result.Type,
                    IsSuccessful = false,
                    Message = $"Test failed: {ex.Message}",
                    Details = new { Error = ex.ToString() }
                };
            }
        }

        private async Task<object> TestFolderSourceAsync(DataSourceConfig dataSource)
        {
            var details = new Dictionary<string, object>();
            var issues = new List<string>();

            // Check if folder exists
            var folderExists = Directory.Exists(dataSource.FolderPath);
            details["FolderExists"] = folderExists;

            if (!folderExists)
            {
                issues.Add($"Folder does not exist: {dataSource.FolderPath}");
            }
            else
            {
                // Check permissions
                try
                {
                    var testFile = Path.Combine(dataSource.FolderPath!, ".test");
                    await System.IO.File.WriteAllTextAsync(testFile, "test"); // Use fully qualified name for File
                    System.IO.File.Delete(testFile); // Use fully qualified name for File
                    details["HasWritePermission"] = true;
                }
                catch
                {
                    details["HasWritePermission"] = false;
                    issues.Add("No write permission to folder");
                }

                // Count existing files
                try
                {
                    var files = Directory.GetFiles(dataSource.FolderPath!, dataSource.FilePattern ?? "*.*");
                    details["ExistingFiles"] = files.Length;
                    details["SampleFiles"] = files.Take(5).Select(Path.GetFileName).ToList();
                }
                catch (Exception ex)
                {
                    issues.Add($"Cannot list files: {ex.Message}");
                }
            }

            var isSuccessful = issues.Count == 0;
            var message = isSuccessful ? "Folder source test passed" : $"Issues found: {string.Join("; ", issues)}";

            return new
            {
                DataSourceId = dataSource.Id,
                Name = dataSource.Name,
                Type = "Folder",
                IsSuccessful = isSuccessful,
                Message = message,
                Details = details
            };
        }

        private async Task<object> TestApiSourceAsync(DataSourceConfig dataSource)
        {
            var details = new Dictionary<string, object>();
            var issues = new List<string>();

            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                // Add API key if configured
                if (!string.IsNullOrEmpty(dataSource.ApiKey))
                {
                    httpClient.DefaultRequestHeaders.Add("X-API-Key", dataSource.ApiKey);
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await httpClient.GetAsync(dataSource.ApiEndpoint);
                stopwatch.Stop();

                details["StatusCode"] = (int)response.StatusCode;
                details["ResponseTime"] = $"{stopwatch.ElapsedMilliseconds}ms";
                details["ContentType"] = response.Content.Headers.ContentType?.MediaType ?? "unknown";

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    details["ContentLength"] = content.Length;

                    // Try to parse as JSON
                    try
                    {
                        using var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                        details["IsValidJson"] = true;
                        details["JsonType"] = jsonDoc.RootElement.ValueKind.ToString();
                    }
                    catch
                    {
                        details["IsValidJson"] = false;
                    }
                }
                else
                {
                    issues.Add($"HTTP {response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (HttpRequestException ex)
            {
                issues.Add($"HTTP error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                issues.Add("Request timed out");
            }
            catch (Exception ex)
            {
                issues.Add($"Unexpected error: {ex.Message}");
            }

            var isSuccessful = issues.Count == 0;
            var message = isSuccessful ? "API source test passed" : $"Issues found: {string.Join("; ", issues)}";

            return new
            {
                DataSourceId = dataSource.Id,
                Name = dataSource.Name,
                Type = "Api",
                IsSuccessful = isSuccessful,
                Message = message,
                Details = details
            };
        }
    }
}
