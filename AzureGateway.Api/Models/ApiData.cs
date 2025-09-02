namespace AzureGateway.Api.Models
{
    public class ApiData
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Type { get; set; }
        public object Data { get; set; }
    }
}
