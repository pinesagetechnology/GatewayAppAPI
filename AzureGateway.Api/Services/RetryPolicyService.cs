using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class RetryPolicyService : IRetryPolicyService
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<RetryPolicyService> _logger;

        public RetryPolicyService(IConfigurationService configService, ILogger<RetryPolicyService> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public async Task<TimeSpan> GetRetryDelayAsync(int attemptCount)
        {
            try
            {
                var baseDelaySeconds = await _configService.GetValueAsync<int?>("Upload.RetryDelaySeconds") ?? 30;
                var maxDelayMinutes = await _configService.GetValueAsync<int?>("Upload.MaxRetryDelayMinutes") ?? 15;

                // Exponential backoff: base * (2^(attempt-1))
                var delaySeconds = baseDelaySeconds * Math.Pow(2, attemptCount - 1);
                var maxDelaySeconds = maxDelayMinutes * 60;

                // Cap at maximum delay
                delaySeconds = Math.Min(delaySeconds, maxDelaySeconds);

                return TimeSpan.FromSeconds(delaySeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating retry delay, using default");
                return TimeSpan.FromMinutes(5); // Safe default
            }
        }

        public async Task<bool> ShouldRetryAsync(int attemptCount, string? errorMessage)
        {
            try
            {
                var maxRetries = await GetMaxRetriesAsync();

                if (attemptCount >= maxRetries)
                    return false;

                // Don't retry certain types of errors
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    var nonRetryableErrors = new[]
                    {
                        "file not found",
                        "access denied",
                        "authentication failed",
                        "invalid blob name",
                        "file too large"
                    };

                    var lowerError = errorMessage.ToLowerInvariant();
                    if (nonRetryableErrors.Any(error => lowerError.Contains(error)))
                    {
                        _logger.LogWarning("Non-retryable error detected: {Error}", errorMessage);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error determining retry policy, defaulting to retry");
                return attemptCount < 3; // Conservative fallback
            }
        }

        public async Task<int> GetMaxRetriesAsync()
        {
            try
            {
                return await _configService.GetValueAsync<int?>("Upload.MaxRetries") ?? 5;
            }
            catch
            {
                return 5; // Default fallback
            }
        }
    }
}
