namespace AzureGateway.Api.Models
{
    public class AzureStorageInfo
    {
        public bool IsConnected { get; set; }
        public string? AccountName { get; set; }
        public List<string> Containers { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
}
