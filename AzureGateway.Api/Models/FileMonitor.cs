namespace AzureGateway.Api.Models
{
    public class FileMonitoringStatus
    {
        public bool IsRunning { get; set; }
        public DateTime StartedAt { get; set; }
        public int ActiveFolderWatchers { get; set; }
        public long TotalFilesProcessed { get; set; }
        public DateTime? LastFileProcessed { get; set; }
        public List<DataSourceStatus> DataSources { get; set; } = new();
    }
}
