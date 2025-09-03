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