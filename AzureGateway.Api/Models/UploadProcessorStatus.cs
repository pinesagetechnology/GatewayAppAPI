namespace AzureGateway.Api.Models
{
    public class UploadProcessorStatus
    {
        public bool IsRunning { get; set; }
        public bool IsPaused { get; set; }
        public DateTime StartedAt { get; set; }
        public int ActiveUploads { get; set; }
        public int PendingCount { get; set; }
        public int FailedCount { get; set; }
        public int CompletedCount { get; set; }
        public long TotalBytesUploaded { get; set; }
        public double AverageUploadSpeedMbps { get; set; }
        public DateTime? LastUploadCompleted { get; set; }
        public List<ActiveUploadInfo> ActiveUploadInfo { get; set; } = new();
        public List<string> RecentErrors { get; set; } = new();
    }

    public class ActiveUploadInfo
    {
        public int UploadId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public double PercentComplete { get; set; }
        public long BytesUploaded { get; set; }
        public long TotalBytes { get; set; }
        public DateTime StartedAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
