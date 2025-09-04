using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using System.Text.Json;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(IServiceProvider serviceProvider, ILogger<ConfigurationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _logger.LogDebug("ConfigurationService initialized");
        }

        private ApplicationDbContext CreateContext()
        {
            var scope = _serviceProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public async Task<string?> GetValueAsync(string key)
        {
            _logger.LogDebug("Getting configuration value for key: {Key}", key);
            using var context = CreateContext();
            var config = await context.Configuration.FindAsync(key);
            
            if (config != null)
            {
                _logger.LogDebug("Found configuration value for key {Key}: {Value}", key, config.Value);
                return config.Value;
            }
            else
            {
                _logger.LogDebug("Configuration key {Key} not found", key);
                return null;
            }
        }

        public async Task<T?> GetValueAsync<T>(string key)
        {
            _logger.LogDebug("Getting typed configuration value for key: {Key}, type: {Type}", key, typeof(T));
            var value = await GetValueAsync(key);
            if (string.IsNullOrEmpty(value))
            {
                _logger.LogDebug("Configuration value for key {Key} is null or empty, returning default", key);
                return default(T);
            }

            try
            {
                T result;
                if (typeof(T) == typeof(string))
                    result = (T)(object)value;
                else if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
                    result = (T)(object)int.Parse(value);
                else if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
                    result = (T)(object)bool.Parse(value);
                else if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
                    result = (T)(object)double.Parse(value);
                else
                    // For complex types, assume JSON
                    result = JsonSerializer.Deserialize<T>(value) ?? default(T)!;

                _logger.LogDebug("Successfully converted configuration value for key {Key} to type {Type}: {Value}", 
                    key, typeof(T), result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting configuration value for key {Key} to type {Type}. Value: {Value}", 
                    key, typeof(T), value);
                return default(T);
            }
        }

        public async Task SetValueAsync(string key, string value, string? description = null, string? category = null)
        {
            _logger.LogDebug("Setting configuration value for key: {Key} = {Value} (Category: {Category})", 
                key, value, category ?? "None");
            
            using var context = CreateContext();
            var existing = await context.Configuration.FindAsync(key);
            
            if (existing != null)
            {
                _logger.LogDebug("Updating existing configuration for key: {Key}", key);
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(description))
                    existing.Description = description;
                if (!string.IsNullOrEmpty(category))
                    existing.Category = category;
            }
            else
            {
                _logger.LogDebug("Creating new configuration for key: {Key}", key);
                var config = new Models.Configuration
                {
                    Key = key,
                    Value = value,
                    Description = description,
                    Category = category,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Configuration.Add(config);
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Configuration updated successfully: {Key} = {Value}", key, value);
        }

        public async Task<Dictionary<string, string>> GetCategoryAsync(string category)
        {
            _logger.LogDebug("Getting all configuration values for category: {Category}", category);
            using var context = CreateContext();
            var configs = await context.Configuration
                .Where(c => c.Category == category)
                .ToListAsync();

            var result = configs.ToDictionary(c => c.Key, c => c.Value);
            _logger.LogDebug("Found {Count} configuration values for category {Category}", result.Count, category);
            return result;
        }

        public async Task<bool> KeyExistsAsync(string key)
        {
            _logger.LogDebug("Checking if configuration key exists: {Key}", key);
            using var context = CreateContext();
            var exists = await context.Configuration.AnyAsync(c => c.Key == key);
            _logger.LogDebug("Configuration key {Key} exists: {Exists}", key, exists);
            return exists;
        }

        public async Task DeleteAsync(string key)
        {
            _logger.LogInformation("Deleting configuration key: {Key}", key);
            using var context = CreateContext();
            var config = await context.Configuration.FindAsync(key);
            if (config != null)
            {
                context.Configuration.Remove(config);
                await context.SaveChangesAsync();
                _logger.LogInformation("Successfully deleted configuration key: {Key}", key);
            }
            else
            {
                _logger.LogWarning("Attempted to delete non-existent configuration key: {Key}", key);
            }
        }

        public async Task<IEnumerable<Models.Configuration>> GetAllAsync()
        {
            _logger.LogDebug("Getting all configuration values");
            using var context = CreateContext();
            var configs = await context.Configuration
                .OrderBy(c => c.Category)
                .ThenBy(c => c.Key)
                .ToListAsync();

            _logger.LogDebug("Retrieved {Count} configuration values", configs.Count);
            return configs;
        }

        public async Task<object> GetQueueSummaryAsync()
        {
            _logger.LogDebug("Getting upload queue summary");
            using var context = CreateContext();
            
            var summary = await context.UploadQueue
                .GroupBy(u => u.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var totalFiles = await context.UploadQueue.CountAsync();
            var totalSize = await context.UploadQueue.SumAsync(u => u.FileSizeBytes);

            var result = new
            {
                TotalFiles = totalFiles,
                TotalSizeBytes = totalSize,
                StatusBreakdown = summary,
                LastUpdated = DateTime.UtcNow
            };

            _logger.LogDebug("Queue summary: {TotalFiles} files, {TotalSize} bytes", totalFiles, totalSize);
            return result;
        }
    }
}
