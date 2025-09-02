namespace AzureGateway.Api.Services.interfaces
{
    public interface IFolderWatcher
    {
        Task StartAsync();
        Task StopAsync();
        bool IsRunning { get; }
    }
}
