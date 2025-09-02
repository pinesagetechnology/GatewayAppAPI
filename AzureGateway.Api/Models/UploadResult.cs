namespace AzureGateway.Api.Models
{
    public class UploadResult
    {
        public bool IsSuccess { get; set; }
        public string? BlobUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public long UploadedBytes { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ETag { get; set; }
    }
}
