using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using System.Text.Json;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ConfigurationService> _logger;

        public ConfigurationService(ApplicationDbContext context, ILogger<ConfigurationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string?> GetValueAsync(string key)
        {
            var config = await _context.Configuration.FindAsync(key);
            return config?.Value;
        }

        public async Task<T?> GetValueAsync<T>(string key)
        {
            var value = await GetValueAsync(key);
            if (string.IsNullOrEmpty(value))
                return default(T);

            try
            {
                if (typeof(T) == typeof(string))
                    return (T)(object)value;
                
                if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
                    return (T)(object)int.Parse(value);
                
                if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
                    return (T)(object)bool.Parse(value);
                
                if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
                    return (T)(object)double.Parse(value);

                // For complex types, assume JSON
                return JsonSerializer.Deserialize<T>(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting configuration value for key {Key} to type {Type}", key, typeof(T));
                return default(T);
            }
        }

        public async Task SetValueAsync(string key, string value, string? description = null, string? category = null)
        {
            var existing = await _context.Configuration.FindAsync(key);
            
            if (existing != null)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(description))
                    existing.Description = description;
                if (!string.IsNullOrEmpty(category))
                    existing.Category = category;
            }
            else
            {
                var config = new Models.Configuration
                {
                    Key = key,
                    Value = value,
                    Description = description,
                    Category = category,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Configuration.Add(config);
            }

            await _context.SaveChangesAsync();
            _logger.LogDebug("Updated configuration: {Key} = {Value}", key, value);
        }

        public async Task<Dictionary<string, string>> GetCategoryAsync(string category)
        {
            var configs = await _context.Configuration
                .Where(c => c.Category == category)
                .ToListAsync();

            return configs.ToDictionary(c => c.Key, c => c.Value);
        }

        public async Task<bool> KeyExistsAsync(string key)
        {
            return await _context.Configuration.AnyAsync(c => c.Key == key);
        }

        public async Task DeleteAsync(string key)
        {
            var config = await _context.Configuration.FindAsync(key);
            if (config != null)
            {
                _context.Configuration.Remove(config);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Deleted configuration key: {Key}", key);
            }
        }

        public async Task<IEnumerable<Models.Configuration>> GetAllAsync()
        {
            return await _context.Configuration
                .OrderBy(c => c.Category)
                .ThenBy(c => c.Key)
                .ToListAsync();
        }
    }
}
