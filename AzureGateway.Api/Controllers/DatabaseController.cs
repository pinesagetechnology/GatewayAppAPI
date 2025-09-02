using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Models;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseController : ControllerBase
    {
        private readonly IUploadQueueService _uploadQueueService;
        private readonly IConfigurationService _configService;
        private readonly ILogger<DatabaseController> _logger;

        public DatabaseController(
            IUploadQueueService uploadQueueService,
            IConfigurationService configService,
            ILogger<DatabaseController> logger)
        {
            _uploadQueueService = uploadQueueService;
            _configService = configService;
            _logger = logger;
        }

        [HttpGet("test")]
        public async Task<IActionResult> TestDatabase()
        {
            try
            {
                // Test basic database operations
                var pendingCount = (await _uploadQueueService.GetPendingUploadsAsync()).Count();
                var failedCount = (await _uploadQueueService.GetFailedUploadsAsync()).Count();
                var configCount = (await _configService.GetAllAsync()).Count();

                var result = new
                {
                    Status = "Success",
                    Message = "Database connection successful",
                    Stats = new
                    {
                        PendingUploads = pendingCount,
                        FailedUploads = failedCount,
                        ConfigurationEntries = configCount
                    },
                    Timestamp = DateTime.UtcNow
                };

                _logger.LogInformation("Database test completed successfully");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database test failed");
                return StatusCode(500, new { Error = "Database test failed", Details = ex.Message });
            }
        }

        [HttpPost("test-upload")]
        public async Task<IActionResult> TestUploadQueue([FromBody] TestUploadRequest request)
        {
            try
            {
                var upload = await _uploadQueueService.AddToQueueAsync(
                    request.FilePath,
                    request.FileType,
                    request.Source,
                    request.FileSize
                );

                _logger.LogInformation("Test upload added: {FileName}", upload.FileName);
                return Ok(new { Message = "Test upload added successfully", UploadId = upload.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add test upload");
                return BadRequest(new { Error = "Failed to add test upload", Details = ex.Message });
            }
        }

        [HttpGet("config")]
        public async Task<IActionResult> GetAllConfig()
        {
            try
            {
                var configs = await _configService.GetAllAsync();
                return Ok(configs.Select(c => new
                {
                    c.Key,
                    c.Value,
                    c.Description,
                    c.Category,
                    c.UpdatedAt
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve configurations");
                return StatusCode(500, new { Error = "Failed to retrieve configurations", Details = ex.Message });
            }
        }

        [HttpPost("config")]
        public async Task<IActionResult> SetConfig([FromBody] SetConfigRequest request)
        {
            try
            {
                await _configService.SetValueAsync(request.Key, request.Value, request.Description, request.Category);
                return Ok(new { Message = "Configuration updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update configuration");
                return BadRequest(new { Error = "Failed to update configuration", Details = ex.Message });
            }
        }
    }

    public class TestUploadRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public FileType FileType { get; set; }
        public DataSource Source { get; set; }
        public long FileSize { get; set; }
    }

    public class SetConfigRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
    }
}