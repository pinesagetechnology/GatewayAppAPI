namespace AzureGateway.Api.Services.interfaces
{
    public interface IConfigurationService
    {
        Task<string?> GetValueAsync(string key);
        Task<T?> GetValueAsync<T>(string key);
        Task SetValueAsync(string key, string value, string? description = null, string? category = null);
        Task<Dictionary<string, string>> GetCategoryAsync(string category);
        Task<bool> KeyExistsAsync(string key);
        Task DeleteAsync(string key);
        Task<IEnumerable<Models.Configuration>> GetAllAsync();
    }
}
