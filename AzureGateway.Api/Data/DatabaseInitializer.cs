using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Models;

namespace AzureGateway.Api.Data
{
    public static class DatabaseInitializer
    {
        public static async Task InitializeAsync(ApplicationDbContext context, ILogger logger)
        {
            logger.LogInformation("=== Starting Database Initialization ===");
            
            try
            {
                // Test database connection first
                logger.LogInformation("Testing database connection...");
                var canConnect = await context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    logger.LogWarning("Cannot connect to database, attempting to create...");
                }
                else
                {
                    logger.LogInformation("Database connection test successful");
                }

                // Ensure database is created
                logger.LogInformation("Ensuring database exists and is up to date...");
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                if (pendingMigrations.Any())
                {
                    logger.LogInformation("Found {Count} pending migrations: {Migrations}", 
                        pendingMigrations.Count(), string.Join(", ", pendingMigrations));
                }
                else
                {
                    logger.LogInformation("No pending migrations found");
                }

                await context.Database.EnsureCreatedAsync();
                logger.LogInformation("Database schema ensured successfully");

                // Check existing tables
                var tableNames = await GetTableNamesAsync(context);
                logger.LogInformation("Database contains {Count} tables: {Tables}", 
                    tableNames.Count, string.Join(", ", tableNames));

                // Check if we need to seed data source configs
                logger.LogInformation("Checking for existing data source configurations...");
                var existingConfigs = await context.DataSourceConfigs.CountAsync();
                logger.LogInformation("Found {Count} existing data source configurations", existingConfigs);

                if (!await context.DataSourceConfigs.AnyAsync())
                {
                    logger.LogInformation("No data source configurations found, seeding defaults...");
                    await SeedDataSourceConfigsAsync(context, logger);
                }
                else
                {
                    logger.LogInformation("Data source configurations already exist, skipping seeding");
                }

                // Log database statistics
                await LogDatabaseStatisticsAsync(context, logger);

                logger.LogInformation("=== Database Initialization Complete ===");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while initializing the database");
                throw;
            }
        }

        private static async Task SeedDataSourceConfigsAsync(ApplicationDbContext context, ILogger logger)
        {
            logger.LogInformation("Seeding default data source configurations...");
            
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

            logger.LogInformation("Adding {Count} default data source configurations", defaultSources.Length);
            foreach (var source in defaultSources)
            {
                logger.LogDebug("Adding data source: {Name} ({Type}) - {Path}", 
                    source.Name, source.SourceType, source.FolderPath ?? source.ApiEndpoint);
            }

            await context.DataSourceConfigs.AddRangeAsync(defaultSources);
            await context.SaveChangesAsync();
            logger.LogInformation("Successfully seeded {Count} default data source configurations", defaultSources.Length);
        }

        public static async Task<bool> TestDatabaseConnectionAsync(ApplicationDbContext context, ILogger logger)
        {
            logger.LogInformation("Testing database connection...");
            try
            {
                var canConnect = await context.Database.CanConnectAsync();
                if (canConnect)
                {
                    logger.LogInformation("Database connection test successful");
                    
                    // Test a simple query
                    var configCount = await context.DataSourceConfigs.CountAsync();
                    logger.LogInformation("Database query test successful - Found {Count} data source configs", configCount);
                }
                else
                {
                    logger.LogWarning("Database connection test failed - Cannot connect to database");
                }
                
                return canConnect;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database connection test failed with exception");
                return false;
            }
        }

        private static async Task<List<string>> GetTableNamesAsync(ApplicationDbContext context)
        {
            try
            {
                var tableNames = new List<string>();
                using var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();
                
                var command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                
                using var result = await command.ExecuteReaderAsync();
                while (await result.ReadAsync())
                {
                    tableNames.Add(result.GetString(0));
                }
                
                return tableNames;
            }
            catch
            {
                return new List<string>();
            }
        }

        private static async Task LogDatabaseStatisticsAsync(ApplicationDbContext context, ILogger logger)
        {
            try
            {
                logger.LogInformation("=== Database Statistics ===");
                
                var configCount = await context.DataSourceConfigs.CountAsync();
                var uploadQueueCount = await context.UploadQueue.CountAsync();
                var uploadHistoryCount = await context.UploadHistory.CountAsync();
                var systemLogCount = await context.SystemLogs.CountAsync();
                
                logger.LogInformation("Data Source Configs: {Count}", configCount);
                logger.LogInformation("Upload Queue Items: {Count}", uploadQueueCount);
                logger.LogInformation("Upload History Items: {Count}", uploadHistoryCount);
                logger.LogInformation("System Logs: {Count}", systemLogCount);
                
                // Check for any failed uploads
                var failedUploads = await context.UploadQueue
                    .Where(u => u.Status == FileStatus.Failed)
                    .CountAsync();
                if (failedUploads > 0)
                {
                    logger.LogWarning("Found {Count} failed uploads in queue", failedUploads);
                }
                
                logger.LogInformation("=== End Database Statistics ===");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not retrieve database statistics");
            }
        }
    }
}
