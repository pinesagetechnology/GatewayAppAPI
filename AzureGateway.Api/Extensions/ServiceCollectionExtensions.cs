using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Services;
using AzureGateway.Api.Data.Repository;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection not found");

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(connectionString);
                options.EnableSensitiveDataLogging(false);
                options.EnableDetailedErrors(true);
            });

            // Register repositories
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            
            // Register services
            services.AddScoped<IUploadQueueService, UploadQueueService>();
            services.AddSingleton<IConfigurationService, ConfigurationService>();

            return services;
        }

        public static async Task<IServiceProvider> SeedConfigurationFromAppSettingsAsync(this IServiceProvider services, IConfiguration configuration)
        {
            using var scope = services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IServiceProvider>>();

            var configDefaults = configuration.GetSection("ConfigurationDefaults");
            if (!configDefaults.Exists())
            {
                logger.LogWarning("ConfigurationDefaults section not found in appsettings.json");
                return services;
            }

            var seededCount = 0;
            foreach (var category in configDefaults.GetChildren())
            {
                foreach (var setting in category.GetChildren())
                {
                    var key = $"{category.Key}.{setting.Key}";
                    var value = setting.Value ?? "";
                    
                    var exists = await configService.KeyExistsAsync(key);
                    if (!exists)
                    {
                        await configService.SetValueAsync(
                            key,
                            value,
                            $"Default value from appsettings.json for {key}",
                            category.Key);
                        
                        seededCount++;
                        logger.LogDebug("Seeded configuration from appsettings: {Key} = {Value}", key, value);
                    }
                }
            }

            logger.LogInformation("Seeded {Count} configuration values from appsettings.json", seededCount);
            return services;
        }

        public static async Task<IServiceProvider> InitializeDatabaseAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

            await DatabaseInitializer.InitializeAsync(context, logger);
            return services;
        }
    }
}
