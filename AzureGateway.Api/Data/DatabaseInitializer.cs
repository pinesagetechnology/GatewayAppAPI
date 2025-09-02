using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Models;

namespace AzureGateway.Api.Data
{
    public static class DatabaseInitializer
    {
        public static async Task InitializeAsync(ApplicationDbContext context, ILogger logger)
        {
            try
            {
                // Ensure database is created
                await context.Database.EnsureCreatedAsync();
                logger.LogInformation("Database initialized successfully");

                // Check if we need to seed data
                if (!await context.Configuration.AnyAsync())
                {
                    await SeedConfigurationDataAsync(context, logger);
                }

                if (!await context.DataSourceConfigs.AnyAsync())
                {
                    await SeedDataSourceConfigsAsync(context, logger);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while initializing the database");
                throw;
            }
        }

        private static async Task SeedConfigurationDataAsync(ApplicationDbContext context, ILogger logger)
        {
            var defaultConfigs = new[]
            {
                new Configuration
                {
                    Key = "Azure.StorageConnectionString",
                    Value = "",
                    Description = "Azure Storage Account connection string",
                    Category = "Azure",
                    IsEncrypted = true,
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Azure.DefaultContainer",
                    Value = "gateway-data",
                    Description = "Default Azure blob container name",
                    Category = "Azure",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Upload.MaxRetries",
                    Value = "5",
                    Description = "Maximum number of retry attempts for failed uploads",
                    Category = "Upload",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Upload.RetryDelaySeconds",
                    Value = "30",
                    Description = "Base delay in seconds between retry attempts (exponential backoff)",
                    Category = "Upload",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Upload.BatchSize",
                    Value = "10",
                    Description = "Number of files to process in each batch",
                    Category = "Upload",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Upload.MaxFileSizeMB",
                    Value = "100",
                    Description = "Maximum file size in MB for uploads",
                    Category = "Upload",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Monitoring.FolderPath",
                    Value = "/home/pi/gateway/incoming",
                    Description = "Default folder path to monitor for new files",
                    Category = "Monitoring",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Monitoring.ArchivePath",
                    Value = "/home/pi/gateway/archive",
                    Description = "Path to move processed files",
                    Category = "Monitoring",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Api.PollingIntervalMinutes",
                    Value = "5",
                    Description = "Interval in minutes for polling third-party API",
                    Category = "Api",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "Api.TimeoutSeconds",
                    Value = "30",
                    Description = "HTTP timeout for API calls in seconds",
                    Category = "Api",
                    UpdatedAt = DateTime.UtcNow
                },
                new Configuration
                {
                    Key = "System.CleanupDays",
                    Value = "30",
                    Description = "Days to keep completed uploads before archiving",
                    Category = "System",
                    UpdatedAt = DateTime.UtcNow
                }
            };

            await context.Configuration.AddRangeAsync(defaultConfigs);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} default configuration entries", defaultConfigs.Length);
        }

        private static async Task SeedDataSourceConfigsAsync(ApplicationDbContext context, ILogger logger)
        {
            var defaultSources = new[]
            {
                new DataSourceConfig
                {
                    Name = "Local Folder Monitor",
                    SourceType = DataSource.Folder,
                    IsEnabled = true,
                    FolderPath = "/home/pi/gateway/incoming",
                    FilePattern = "*.{json,jpg,jpeg,png}",
                    PollingIntervalMinutes = 1,
                    CreatedAt = DateTime.UtcNow
                },
                new DataSourceConfig
                {
                    Name = "Third Party API",
                    SourceType = DataSource.Api,
                    IsEnabled = false,
                    ApiEndpoint = "https://api.example.com/data",
                    PollingIntervalMinutes = 5,
                    CreatedAt = DateTime.UtcNow,
                    AdditionalSettings = "{\"headers\": {\"Accept\": \"application/json\"}}"
                }
            };

            await context.DataSourceConfigs.AddRangeAsync(defaultSources);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} default data source configurations", defaultSources.Length);
        }

        public static async Task<bool> TestDatabaseConnectionAsync(ApplicationDbContext context, ILogger logger)
        {
            try
            {
                await context.Database.CanConnectAsync();
                logger.LogInformation("Database connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database connection test failed");
                return false;
            }
        }
    }
}
