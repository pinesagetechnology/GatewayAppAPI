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

                // Check if we need to seed data source configs
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

        private static async Task SeedDataSourceConfigsAsync(ApplicationDbContext context, ILogger logger)
        {
            var defaultSources = new[]
            {
                new DataSourceConfig
                {
                    Name = "Local Folder Monitor",
                    SourceType = DataSource.Folder,
                    IsEnabled = true,
                    FolderPath = "C:\\workspace\\PineSageProjects\\incoming",
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
