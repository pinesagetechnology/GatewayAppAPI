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

    

    public class SetConfigRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
    }
}