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
            services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddSingleton<IApiPollingService, ApiPollingService>();

            return services;
        }

        public static async Task<IServiceProvider> SeedConfigurationFromAppSettingsAsync(this IServiceProvider services, IConfiguration configuration)
        {
            using var scope = services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IServiceProvider>>();

            logger.LogInformation("=== Starting Configuration Seeding ===");
            
            var configDefaults = configuration.GetSection("ConfigurationDefaults");
            if (!configDefaults.Exists())
            {
                logger.LogWarning("ConfigurationDefaults section not found in appsettings.json");
                return services;
            }

            logger.LogInformation("Found ConfigurationDefaults section with {CategoryCount} categories", 
                configDefaults.GetChildren().Count());

            var seededCount = 0;
            var skippedCount = 0;
            var errorCount = 0;

            foreach (var category in configDefaults.GetChildren())
            {
                logger.LogInformation("Processing configuration category: {Category}", category.Key);
                var categorySettings = category.GetChildren().ToList();
                logger.LogInformation("Category {Category} contains {SettingCount} settings", 
                    category.Key, categorySettings.Count);

                foreach (var setting in categorySettings)
                {
                    var key = $"{category.Key}.{setting.Key}";
                    var value = setting.Value ?? "";
                    
                    try
                    {
                        var exists = await configService.KeyExistsAsync(key);
                        if (!exists)
                        {
                            await configService.SetValueAsync(
                                key,
                                value,
                                $"Default value from appsettings.json for {key}",
                                category.Key);
                            
                            seededCount++;
                            logger.LogDebug("Seeded configuration: {Key} = {Value}", key, value);
                        }
                        else
                        {
                            skippedCount++;
                            logger.LogDebug("Configuration key {Key} already exists, skipping", key);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        logger.LogError(ex, "Failed to seed configuration key: {Key}", key);
                    }
                }
            }

            logger.LogInformation("=== Configuration Seeding Complete ===");
            logger.LogInformation("Seeded: {SeededCount}, Skipped: {SkippedCount}, Errors: {ErrorCount}", 
                seededCount, skippedCount, errorCount);
            
            return services;
        }

        public static async Task<IServiceProvider> InitializeDatabaseAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

            logger.LogInformation("=== Starting Database Initialization ===");
            
            try
            {
                await DatabaseInitializer.InitializeAsync(context, logger);
                logger.LogInformation("Database initialization completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database initialization failed");
                throw;
            }

            return services;
        }

        public static async Task<bool> ValidateAzureConfigurationAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IServiceProvider>>();

            logger.LogInformation("=== Validating Azure Configuration ===");
            
            try
            {
                var connectionString = await configService.GetValueAsync("Azure.StorageConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    logger.LogWarning("Azure Storage connection string is not configured");
                    return false;
                }

                logger.LogInformation("Azure Storage connection string found");
                
                // Check if it's a valid connection string format
                if (connectionString.Contains("AccountName=") && connectionString.Contains("AccountKey="))
                {
                    logger.LogInformation("Azure Storage connection string format appears valid");
                    return true;
                }
                else
                {
                    logger.LogWarning("Azure Storage connection string format appears invalid");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating Azure configuration");
                return false;
            }
        }
    }
}
