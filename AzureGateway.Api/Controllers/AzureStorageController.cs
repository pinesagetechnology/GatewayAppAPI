using AzureGateway.Api.Services.interfaces;
using AzureGateway.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Models;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AzureStorageController : ControllerBase
    {
        private readonly IAzureStorageService _azureService;
        private readonly IConfigurationService _configService;
        private readonly ILogger<AzureStorageController> _logger;

        public AzureStorageController(
            IAzureStorageService azureService,
            IConfigurationService configService,
            ILogger<AzureStorageController> logger)
        {
            _azureService = azureService;
            _configService = configService;
            _logger = logger;
        }

        [HttpGet("info")]
        public async Task<IActionResult> GetStorageInfo()
        {
            try
            {
                var info = await _azureService.GetStorageInfoAsync();
                return Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Azure Storage info");
                return StatusCode(500, new { Error = "Failed to get storage info", Details = ex.Message });
            }
        }

        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var isConnected = await _azureService.IsConnectedAsync();
                var result = new
                {
                    IsConnected = isConnected,
                    Status = isConnected ? "Connected" : "Not Connected",
                    TestedAt = DateTime.UtcNow
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test Azure connection");
                return StatusCode(500, new { Error = "Connection test failed", Details = ex.Message });
            }
        }

        [HttpGet("containers")]
        public async Task<IActionResult> ListContainers()
        {
            try
            {
                var info = await _azureService.GetStorageInfoAsync();
                if (!info.IsConnected)
                {
                    return BadRequest(new { Error = "Not connected to Azure Storage", Details = info.ErrorMessage });
                }

                return Ok(new { Containers = info.Containers });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list containers");
                return StatusCode(500, new { Error = "Failed to list containers", Details = ex.Message });
            }
        }

        [HttpGet("containers/{containerName}/blobs")]
        public async Task<IActionResult> ListBlobs(string containerName, [FromQuery] string? prefix = null)
        {
            try
            {
                var blobs = await _azureService.ListBlobsAsync(containerName, prefix);
                return Ok(new { Container = containerName, Prefix = prefix, Blobs = blobs });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list blobs in container {ContainerName}", containerName);
                return StatusCode(500, new { Error = "Failed to list blobs", Details = ex.Message });
            }
        }

        [HttpPost("containers/{containerName}")]
        public async Task<IActionResult> CreateContainer(string containerName)
        {
            try
            {
                var success = await _azureService.CreateContainerIfNotExistsAsync(containerName);
                if (success)
                {
                    return Ok(new { Message = $"Container '{containerName}' created or already exists" });
                }
                else
                {
                    return StatusCode(500, new { Error = "Failed to create container" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create container {ContainerName}", containerName);
                return StatusCode(500, new { Error = "Failed to create container", Details = ex.Message });
            }
        }

        [HttpDelete("containers/{containerName}/blobs/{blobName}")]
        public async Task<IActionResult> DeleteBlob(string containerName, string blobName)
        {
            try
            {
                var success = await _azureService.DeleteBlobAsync(containerName, blobName);
                if (success)
                {
                    return Ok(new { Message = $"Blob '{blobName}' deleted successfully" });
                }
                else
                {
                    return NotFound(new { Error = "Blob not found or could not be deleted" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete blob {BlobName} from {ContainerName}", blobName, containerName);
                return StatusCode(500, new { Error = "Failed to delete blob", Details = ex.Message });
            }
        }

        [HttpPost("test-upload")]
        public async Task<IActionResult> TestUpload([FromBody] AzureTestUploadRequest request)
        {
            try
            {
                var testData = System.Text.Encoding.UTF8.GetBytes(request.Content ?? "Test upload data");
                var fileName = request.FileName ?? $"test_upload_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
                var containerName = request.ContainerName ??
                    await _configService.GetValueAsync("Azure.DefaultContainer") ?? "gateway-data";

                var result = await _azureService.UploadDataAsync(testData, fileName, containerName);

                if (result.IsSuccess)
                {
                    _logger.LogInformation("Test upload successful: {BlobUrl}", result.BlobUrl);
                    return Ok(new
                    {
                        Success = true,
                        Message = "Test upload completed successfully",
                        BlobUrl = result.BlobUrl,
                        UploadedBytes = result.UploadedBytes,
                        Duration = result.Duration.TotalMilliseconds
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Error = result.ErrorMessage,
                        Message = "Test upload failed"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test upload failed");
                return StatusCode(500, new { Error = "Test upload failed", Details = ex.Message });
            }
        }

        [HttpPost("configure")]
        public async Task<IActionResult> ConfigureAzureStorage([FromBody] AzureStorageConfigRequest request)
        {
            try
            {
                // Update Azure Storage configuration
                await _configService.SetValueAsync("Azure.StorageConnectionString", request.ConnectionString,
                    "Azure Storage Account connection string", "Azure");

                if (!string.IsNullOrEmpty(request.DefaultContainer))
                {
                    await _configService.SetValueAsync("Azure.DefaultContainer", request.DefaultContainer,
                        "Default Azure blob container name", "Azure");
                }

                if (request.MaxConcurrentUploads.HasValue)
                {
                    await _configService.SetValueAsync("Azure.MaxConcurrentUploads", request.MaxConcurrentUploads.Value.ToString(),
                        "Maximum number of concurrent uploads", "Azure");
                }

                // Test the new configuration
                var testService = new AzureStorageService(_configService, _logger);
                var isConnected = await testService.IsConnectedAsync();

                return Ok(new
                {
                    Message = "Azure Storage configuration updated",
                    ConnectionTest = isConnected ? "Success" : "Failed",
                    IsConnected = isConnected
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure Azure Storage");
                return StatusCode(500, new { Error = "Configuration failed", Details = ex.Message });
            }
        }
    }
}
