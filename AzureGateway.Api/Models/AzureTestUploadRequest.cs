namespace AzureGateway.Api.Models
{
    public class AzureTestUploadRequest
    {
        public string? FileName { get; set; }
        public string? ContainerName { get; set; }
        public string? Content { get; set; }
    }

}
