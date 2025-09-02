namespace AzureGateway.Api.Models
{
    public class AzureStorageConfigRequest
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string? DefaultContainer { get; set; }
        public int? MaxConcurrentUploads { get; set; }
    }
}
