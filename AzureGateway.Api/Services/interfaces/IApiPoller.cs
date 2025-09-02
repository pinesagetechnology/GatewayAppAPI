namespace AzureGateway.Api.Services.interfaces
{
    public interface IApiPoller
    {
        Task StartAsync();
        Task StopAsync();
        bool IsRunning { get; }
    }
}