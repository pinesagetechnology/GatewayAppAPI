using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly IDatabaseHealthService _healthService;
        private readonly ILogger<HealthController> _logger;

        public HealthController(IDatabaseHealthService healthService, ILogger<HealthController> logger)
        {
            _healthService = healthService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetHealth()
        {
            try
            {
                var health = await _healthService.CheckHealthAsync();
                var statusCode = health.IsHealthy ? 200 : 503;
                return StatusCode(statusCode, health);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check endpoint failed");
                return StatusCode(500, new { Status = "Error", Message = ex.Message });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var stats = await _healthService.GetDatabaseStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stats endpoint failed");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        [HttpPost("cleanup")]
        public async Task<IActionResult> RunCleanup()
        {
            try
            {
                await _healthService.CleanupOldDataAsync();
                return Ok(new { Message = "Database cleanup completed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database cleanup failed");
                return StatusCode(500, new { Error = "Cleanup failed", Details = ex.Message });
            }
        }

        [HttpPost("test-operations")]
        public async Task<IActionResult> TestOperations()
        {
            try
            {
                var success = await _healthService.TestAllOperationsAsync();
                return Ok(new { Success = success, Message = success ? "All operations passed" : "Some operations failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database operations test failed");
                return StatusCode(500, new { Error = "Operations test failed", Details = ex.Message });
            }
        }
    }
}