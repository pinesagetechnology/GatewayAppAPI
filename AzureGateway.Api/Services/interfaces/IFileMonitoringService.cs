using AzureGateway.Api.Models;

namespace AzureGateway.Api.Services.interfaces
{
    public interface IFileMonitoringService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task<bool> IsRunningAsync();
        Task<FileMonitoringStatus> GetStatusAsync();
        Task RefreshDataSourcesAsync();
    }
    public class FileMonitoringStatus
    {
        public bool IsRunning { get; set; }
        public DateTime StartedAt { get; set; }
        public int ActiveFolderWatchers { get; set; }
        public int ActiveApiPollers { get; set; }
        public long TotalFilesProcessed { get; set; }
        public DateTime? LastFileProcessed { get; set; }
        public List<DataSourceStatus> DataSources { get; set; } = new();
    }
    public class DataSourceStatus
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DataSource Type { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastActivity { get; set; }
        public long FilesProcessed { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastErrorAt { get; set; }
    }
}