using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IDatabaseHealthService
    {
        Task<DatabaseHealthStatus> CheckHealthAsync();
        Task<bool> CanConnectAsync();
        Task<Dictionary<string, object>> GetDatabaseStatsAsync();
        Task CleanupOldDataAsync();
        Task<bool> TestAllOperationsAsync();
    }

    public class DatabaseHealthStatus
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = string.Empty;
        public Dictionary<string, object> Stats { get; set; } = new();
        public List<string> Issues { get; set; } = new();
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    }
}
