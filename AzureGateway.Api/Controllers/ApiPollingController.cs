using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApiPollingController : ControllerBase
    {
        private readonly IApiPollingService _pollingService;
        private readonly ILogger<ApiPollingController> _logger;

        public ApiPollingController(IApiPollingService pollingService, ILogger<ApiPollingController> logger)
        {
            _pollingService = pollingService;
            _logger = logger;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                var status = _pollingService.GetStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get API polling status");
                return StatusCode(500, new { Error = "Failed to get status", Details = ex.Message });
            }
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            try
            {
                await _pollingService.StartAsync(CancellationToken.None);
                return Ok(new { Message = "API polling started successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start API polling");
                return StatusCode(500, new { Error = "Failed to start API polling", Details = ex.Message });
            }
        }

        [HttpPost("stop")]
        public async Task<IActionResult> Stop()
        {
            try
            {
                await _pollingService.StopAsync(CancellationToken.None);
                return Ok(new { Message = "API polling stopped successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop API polling");
                return StatusCode(500, new { Error = "Failed to stop API polling", Details = ex.Message });
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            try
            {
                await _pollingService.RefreshDataSourcesAsync();
                return Ok(new { Message = "API data sources refreshed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh API data sources");
                return StatusCode(500, new { Error = "Failed to refresh data sources", Details = ex.Message });
            }
        }
    }
}

