using AzureGateway.Api.Models;
using AzureGateway.Api.Utilities;
using System.Text.Json;
using System.Text;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class ApiPoller : IApiPoller, IDisposable
    {
        private readonly DataSourceConfig _config;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<int, string, Task> _onFileProcessed;
        private readonly Func<int, string, Task> _onError;
        private readonly ILogger<ApiPoller> _logger;
        private readonly HttpClient _httpClient;

        private Timer? _pollingTimer;
        private bool _isRunning = false;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private DateTime _lastPollTime = DateTime.MinValue;

        public bool IsRunning => _isRunning;

        public ApiPoller(
            DataSourceConfig config,
            IServiceProvider serviceProvider,
            Func<int, string, Task> onFileProcessed,
            Func<int, string, Task> onError)
        {
            _config = config;
            _serviceProvider = serviceProvider;
            _onFileProcessed = onFileProcessed;
            _onError = onError;
            _logger = serviceProvider.GetRequiredService<ILogger<ApiPoller>>();

            _httpClient = new HttpClient();
            ConfigureHttpClient();
        }

        public async Task StartAsync()
        {
            if (_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (_isRunning) return;
                _logger.LogInformation("Starting API poller for endpoint: {Endpoint}", _config.ApiEndpoint);

                if (string.IsNullOrEmpty(_config.ApiEndpoint))
                {
                    var error = "API endpoint is not configured";
                    await _onError(_config.Id, error);
                    throw new InvalidOperationException(error);
                }

                var interval = TimeSpan.FromMinutes(_config.PollingIntervalMinutes);
                _pollingTimer = new Timer(async _ => await PollApiAsync(), null, TimeSpan.Zero, interval);
                _isRunning = true;

                _logger.LogInformation("Started API poller for {Name} polling {Endpoint} every {Minutes} minutes",
                    _config.Name, _config.ApiEndpoint, _config.PollingIntervalMinutes);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (!_isRunning) return;

                _pollingTimer?.Dispose();
                _pollingTimer = null;
                _isRunning = false;

                _logger.LogInformation("Stopped API poller for {Name}", _config.Name);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void ConfigureHttpClient()
        {
            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

            // Set timeout
            var timeoutSeconds = configService.GetValueAsync<int?>("Api.TimeoutSeconds").Result ?? 30;
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Add API key if configured
            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _config.ApiKey);
            }

            // Add custom headers from additional settings
            if (!string.IsNullOrEmpty(_config.AdditionalSettings))
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<ApiPollerSettings>(_config.AdditionalSettings);
                    if (settings?.Headers != null)
                    {
                        foreach (var header in settings.Headers)
                        {
                            _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse additional settings for {Name}", _config.Name);
                }
            }

            // Set user agent
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AzureGateway/1.0");
        }

        private async Task PollApiAsync()
        {
            if (!_isRunning) return;

            try
            {
                _logger.LogDebug("Polling API {Name} at {Endpoint}", _config.Name, _config.ApiEndpoint);

                var response = await _httpClient.GetAsync(_config.ApiEndpoint);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLower();

                await ProcessApiResponseAsync(content, contentType);

                _lastPollTime = DateTime.UtcNow;

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();
                var dataSource = await context.DataSourceConfigs.FindAsync(_config.Id);
                if (dataSource != null)
                {
                    dataSource.LastProcessedAt = _lastPollTime;
                    await context.SaveChangesAsync();
                }
            }
            catch (HttpRequestException ex)
            {
                var error = $"HTTP error polling API: {ex.Message}";
                await _onError(_config.Id, error);
                _logger.LogError(ex, "HTTP error polling {Name}", _config.Name);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                var error = "API request timed out";
                await _onError(_config.Id, error);
                _logger.LogError("Timeout polling {Name} at {Endpoint}", _config.Name, _config.ApiEndpoint);
            }
            catch (Exception ex)
            {
                var error = $"Unexpected error polling API: {ex.Message}";
                await _onError(_config.Id, error);
                _logger.LogError(ex, "Unexpected error polling {Name}", _config.Name);
            }
        }

        private async Task ProcessApiResponseAsync(string content, string? contentType)
        {
            try
            {
                // Determine how to process based on content type and response structure
                if (IsJsonContent(contentType))
                {
                    await ProcessJsonResponseAsync(content);
                }
                else
                {
                    // Handle other content types or treat as raw data
                    await ProcessRawResponseAsync(content, contentType ?? "text/plain");
                }
            }
            catch (Exception ex)
            {
                var error = $"Error processing API response: {ex.Message}";
                await _onError(_config.Id, error);
                throw;
            }
        }

        private async Task ProcessJsonResponseAsync(string jsonContent)
        {
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;

            // Check if response contains an array of items or single item
            if (root.ValueKind == JsonValueKind.Array)
            {
                await ProcessJsonArrayAsync(root);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Check if object contains a data array
                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    await ProcessJsonArrayAsync(dataElement);
                }
                else if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    await ProcessJsonArrayAsync(itemsElement);
                }
                else
                {
                    // Process single object
                    await ProcessSingleJsonItemAsync(root);
                }
            }
        }

        private async Task ProcessJsonArrayAsync(JsonElement arrayElement)
        {
            var itemCount = 0;
            foreach (var item in arrayElement.EnumerateArray())
            {
                await ProcessSingleJsonItemAsync(item);
                itemCount++;
            }

            _logger.LogDebug("Processed {ItemCount} JSON items from API {Name}", itemCount, _config.Name);
        }

        private async Task ProcessSingleJsonItemAsync(JsonElement item)
        {
            // Generate a unique filename for this JSON item
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var itemId = ExtractItemId(item) ?? Guid.NewGuid().ToString("N")[..8];
            var fileName = $"api_data_{_config.Name}_{timestamp}_{itemId}.json";

            // Serialize the item back to JSON
            var jsonString = JsonSerializer.Serialize(item, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await ProcessDataAsync(jsonString, fileName, FileType.Json);
        }

        private async Task ProcessRawResponseAsync(string content, string contentType)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var extension = GetFileExtensionFromContentType(contentType);
            var fileName = $"api_response_{_config.Name}_{timestamp}.{extension}";
            var fileType = FileHelper.GetFileType(fileName);

            await ProcessDataAsync(content, fileName, fileType);
        }

        private async Task ProcessDataAsync(string content, string fileName, FileType fileType)
        {
            try
            {
                // Create temporary file to store the data
                var tempDir = await GetTempDirectoryAsync();
                var tempFilePath = Path.Combine(tempDir, fileName);

                await File.WriteAllTextAsync(tempFilePath, content, Encoding.UTF8);

                // Calculate hash for duplicate detection
                var hash = await FileHelper.CalculateFileHashAsync(tempFilePath);
                var fileSize = new FileInfo(tempFilePath).Length;

                // Add to upload queue
                using var scope = _serviceProvider.CreateScope();
                var uploadService = scope.ServiceProvider.GetRequiredService<IUploadQueueService>();

                // Check for duplicates
                if (await uploadService.IsDuplicateAsync(hash))
                {
                    _logger.LogDebug("Duplicate data detected from API {Name}, skipping", _config.Name);
                    File.Delete(tempFilePath);
                    return;
                }

                var upload = await uploadService.AddToQueueAsync(
                    tempFilePath, fileType, DataSource.Api, fileSize, hash);

                _logger.LogInformation("Added API data to upload queue: {FileName} (ID: {UploadId})", fileName, upload.Id);

                await _onFileProcessed(_config.Id, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing API data for {Name}", _config.Name);
                throw;
            }
        }

        private string? ExtractItemId(JsonElement item)
        {
            // Try common ID field names
            var idFields = new[] { "id", "Id", "ID", "identifier", "key", "uuid" };

            foreach (var field in idFields)
            {
                if (item.TryGetProperty(field, out var idElement))
                {
                    return idElement.ValueKind switch
                    {
                        JsonValueKind.String => idElement.GetString(),
                        JsonValueKind.Number => idElement.GetInt64().ToString(),
                        _ => null
                    };
                }
            }

            return null;
        }

        private static string GetFileExtensionFromContentType(string contentType)
        {
            return contentType.ToLower() switch
            {
                "application/json" => "json",
                "text/plain" => "txt",
                "text/csv" => "csv",
                "application/xml" or "text/xml" => "xml",
                "image/jpeg" => "jpg",
                "image/png" => "png",
                _ => "data"
            };
        }

        private static bool IsJsonContent(string? contentType)
        {
            return contentType?.Contains("application/json") == true ||
                   contentType?.Contains("text/json") == true;
        }

        private async Task<string> GetTempDirectoryAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

            var tempPath = await configService.GetValueAsync("Api.TempDirectory") ??
                           Path.Combine(Path.GetTempPath(), "azure-gateway", "api-data");

            if (!Directory.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }

            return tempPath;
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _pollingTimer?.Dispose();
            _httpClient?.Dispose();
            _semaphore?.Dispose();
        }
    }

    // Supporting classes for API polling configuration
    public class ApiPollerSettings
    {
        public Dictionary<string, string>? Headers { get; set; }
        public string? AuthenticationType { get; set; }
        public string? AuthenticationValue { get; set; }
        public int? RetryCount { get; set; }
        public int? RetryDelayMs { get; set; }
        public bool ParseJsonArray { get; set; } = true;
        public string? ItemIdField { get; set; }
        public string? TimestampField { get; set; }
        public string? DataField { get; set; }
    }
}