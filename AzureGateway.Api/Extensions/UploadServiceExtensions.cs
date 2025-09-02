using AzureGateway.Api.Services.interfaces;
using AzureGateway.Api.Services;
using AzureGateway.Api.HostedServices;

namespace AzureGateway.Api.Extensions
{
    public static class UploadServiceExtensions
    {
        public static IServiceCollection AddUploadServices(this IServiceCollection services)
        {
            // Register Azure Storage service
            services.AddSingleton<IAzureStorageService, AzureStorageService>();

            // Register upload processor service
            services.AddSingleton<IUploadProcessorService, UploadProcessorService>();

            // Register retry policy service
            services.AddScoped<IRetryPolicyService, RetryPolicyService>();

            // Register hosted service to manage upload processing lifecycle
            services.AddHostedService<UploadProcessorHostedService>();

            return services;
        }

        public static async Task<IServiceProvider> SeedUploadConfigAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IServiceProvider>>();

            // Add upload processing specific configurations
            var defaultConfigs = new Dictionary<string, (string value, string description, string category)>
            {
                ["Upload.AutoStart"] = ("true", "Automatically start upload processor on application startup", "Upload"),
                ["Upload.ProcessingIntervalSeconds"] = ("10", "Interval in seconds between upload processing cycles", "Upload"),
                ["Upload.MaxConcurrentUploads"] = ("3", "Maximum number of concurrent uploads", "Upload"),
                ["Upload.MaxRetries"] = ("5", "Maximum number of retry attempts for failed uploads", "Upload"),
                ["Upload.RetryDelaySeconds"] = ("30", "Base delay in seconds between retry attempts", "Upload"),
                ["Upload.MaxRetryDelayMinutes"] = ("15", "Maximum delay in minutes between retries", "Upload"),
                ["Azure.StorageConnectionString"] = ("", "Azure Storage Account connection string (encrypted)", "Azure"),
                ["Azure.DefaultContainer"] = ("gateway-data", "Default Azure blob container name", "Azure"),
                ["Azure.MaxConcurrentUploads"] = ("3", "Maximum concurrent uploads to Azure", "Azure"),
                ["Azure.ChunkSizeBytes"] = ("4194304", "Upload chunk size in bytes (4MB)", "Azure"),
                ["Upload.ArchiveOnSuccess"] = ("true", "Move source files to archive after successful upload", "Upload"),
                ["Upload.DeleteOnSuccess"] = ("false", "Delete source files after successful upload (use with caution)", "Upload"),
                ["Upload.NotifyOnCompletion"] = ("true", "Send notifications when uploads complete", "Upload"),
                ["Upload.NotifyOnFailure"] = ("true", "Send notifications when uploads fail", "Upload")
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

                    logger.LogDebug("Added upload configuration: {Key}", config.Key);
                }
            }

            return services;
        }

        public static async Task<bool> ValidateAzureConfigurationAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var azureService = scope.ServiceProvider.GetRequiredService<IAzureStorageService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IServiceProvider>>();

            try
            {
                var isConnected = await azureService.IsConnectedAsync();
                if (isConnected)
                {
                    logger.LogInformation("Azure Storage connection validated successfully");

                    // Test creating default container
                    var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                    var defaultContainer = await configService.GetValueAsync("Azure.DefaultContainer") ?? "gateway-data";
                    await azureService.CreateContainerIfNotExistsAsync(defaultContainer);

                    return true;
                }
                else
                {
                    logger.LogWarning("Azure Storage connection validation failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Azure Storage validation error");
                return false;
            }
        }
    }
}