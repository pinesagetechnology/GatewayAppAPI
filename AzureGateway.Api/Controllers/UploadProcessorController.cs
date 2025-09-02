// Controllers/UploadProcessorController.cs
using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadProcessorController : ControllerBase
    {
        private readonly IUploadProcessorService _processorService;
        private readonly IUploadQueueService _queueService;
        private readonly ILogger<UploadProcessorController> _logger;

        public UploadProcessorController(
            IUploadProcessorService processorService,
            IUploadQueueService queueService,
            ILogger<UploadProcessorController> logger)
        {
            _processorService = processorService;
            _queueService = queueService;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = await _processorService.GetStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get processor status");
                return StatusCode(500, new { Error = "Failed to get status", Details = ex.Message });
            }
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            try
            {
                await _processorService.StartAsync(CancellationToken.None);
                return Ok(new { Message = "Upload processor started successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start upload processor");
                return StatusCode(500, new { Error = "Failed to start processor", Details = ex.Message });
            }
        }

        [HttpPost("stop")]
        public async Task<IActionResult> Stop()
        {
            try
            {
                await _processorService.StopAsync(CancellationToken.None);
                return Ok(new { Message = "Upload processor stopped successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop upload processor");
                return StatusCode(500, new { Error = "Failed to stop processor", Details = ex.Message });
            }
        }

        [HttpPost("pause")]
        public async Task<IActionResult> Pause()
        {
            try
            {
                await _processorService.PauseProcessingAsync();
                return Ok(new { Message = "Upload processing paused" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pause upload processor");
                return StatusCode(500, new { Error = "Failed to pause processor", Details = ex.Message });
            }
        }

        [HttpPost("resume")]
        public async Task<IActionResult> Resume()
        {
            try
            {
                await _processorService.ResumeProcessingAsync();
                return Ok(new { Message = "Upload processing resumed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume upload processor");
                return StatusCode(500, new { Error = "Failed to resume processor", Details = ex.Message });
            }
        }

        [HttpPost("process-now")]
        public async Task<IActionResult> ProcessNow([FromQuery] int maxConcurrent = 3)
        {
            try
            {
                await _processorService.ProcessPendingUploadsAsync(maxConcurrent);
                return Ok(new { Message = "Manual processing triggered successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger manual processing");
                return StatusCode(500, new { Error = "Failed to process uploads", Details = ex.Message });
            }
        }

        [HttpPost("retry-failed")]
        public async Task<IActionResult> RetryFailed()
        {
            try
            {
                await _processorService.RetryFailedUploadsAsync();
                return Ok(new { Message = "Failed uploads reset for retry" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retry failed uploads");
                return StatusCode(500, new { Error = "Failed to retry uploads", Details = ex.Message });
            }
        }

        [HttpGet("queue-summary")]
        public async Task<IActionResult> GetQueueSummary()
        {
            try
            {
                var pending = await _queueService.GetPendingUploadsAsync();
                var failed = await _queueService.GetFailedUploadsAsync();
                var recent = await _queueService.GetRecentUploadsAsync(20);

                var summary = new
                {
                    PendingCount = pending.Count(),
                    FailedCount = failed.Count(),
                    RecentUploads = recent.Select(u => new
                    {
                        u.Id,
                        u.FileName,
                        u.Status,
                        u.CreatedAt,
                        u.CompletedAt,
                        u.FileSizeBytes,
                        u.AttemptCount,
                        u.ErrorMessage,
                        ProgressPercent = u.UploadProgresses.LastOrDefault()?.PercentageComplete ?? 0
                    }),
                    TotalSizeBytes = pending.Sum(u => u.FileSizeBytes) + failed.Sum(u => u.FileSizeBytes),
                    OldestPending = pending.MinBy(u => u.CreatedAt)?.CreatedAt,
                    NewestPending = pending.MaxBy(u => u.CreatedAt)?.CreatedAt
                };

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get queue summary");
                return StatusCode(500, new { Error = "Failed to get queue summary", Details = ex.Message });
            }
        }
    }
}