namespace AzureGateway.Api.Services.interfaces
{
    public interface IRetryPolicyService
    {
        Task<TimeSpan> GetRetryDelayAsync(int attemptCount);
        Task<bool> ShouldRetryAsync(int attemptCount, string? errorMessage);
        Task<int> GetMaxRetriesAsync();
    }
}
