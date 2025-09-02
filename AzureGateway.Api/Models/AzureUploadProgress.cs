namespace AzureGateway.Api.Models
{
    public class AzureUploadProgress
    {
        public long BytesUploaded { get; set; }
        public long TotalBytes { get; set; }
        public double PercentComplete => TotalBytes > 0 ? (double)BytesUploaded / TotalBytes * 100 : 0;
        public string? StatusMessage { get; set; }
    }
}
