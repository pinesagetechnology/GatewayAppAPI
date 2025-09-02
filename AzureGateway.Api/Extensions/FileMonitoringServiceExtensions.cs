using AzureGateway.Api.Services;
using AzureGateway.Api.HostedServices;
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

            return services;
        }

        public static async Task<IServiceProvider> SeedFileMonitoringConfigAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IServiceProvider>>();

            // Add file monitoring specific configurations
            var defaultConfigs = new Dictionary<string, (string value, string description, string category)>
            {
                ["FileMonitoring.AutoStart"] = ("true", "Automatically start file monitoring on application startup", "FileMonitoring"),
                ["FileMonitoring.DefaultFilePattern"] = ("*.{json,jpg,jpeg,png}", "Default file pattern for folder monitoring", "FileMonitoring"),
                ["FileMonitoring.ProcessExistingFiles"] = ("true", "Process existing files when starting folder monitoring", "FileMonitoring"),
                ["Api.TempDirectory"] = ("/tmp/azure-gateway/api-data", "Temporary directory for API-sourced files", "Api"),
                ["Api.MaxResponseSizeMB"] = ("50", "Maximum API response size in MB", "Api"),
                ["Api.DefaultPollingInterval"] = ("5", "Default polling interval in minutes for API sources", "Api")
            };

            foreach (var config in defaultConfigs)
            {
                var exists = await configService.KeyExistsAsync(config.Key);
                if (!exists)
                {
                    await configService.SetValueAsync(
                        config.Key,
                        config.Value.value,
                        config.Value.description,
                        config.Value.category);

                    logger.LogDebug("Added file monitoring configuration: {Key}", config.Key);
                }
            }

            return services;
        }
    }
}