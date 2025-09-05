using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Extensions
{
    public static class FileMonitoringServiceExtensions
    {
        public static IServiceCollection AddFileMonitoring(this IServiceCollection services)
        {
            // Register file monitoring services
            services.AddSingleton<IFileMonitoringService, FileMonitoringService>();

            // Register hosted service to manage file monitoring lifecycle
            services.AddHostedService<FileMonitoringHostedService>();

            // Register API polling hosted service (auto-start controlled by config)
            services.AddHostedService<AzureGateway.Api.HostedServices.ApiPollingHostedService>();

            return services;
        }
    }
}