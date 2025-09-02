using Microsoft.AspNetCore.Mvc;
using AzureGateway.Api.Utilities;
using System.Text.Json;
using AzureGateway.Api.Services.interfaces;
using AzureGateway.Api.Models;

namespace AzureGateway.Api.Controllers
{
    [ApiController]
    [Route("api/test/[controller]")]
    public class FileMonitoringTestController : ControllerBase
    {
        private readonly IUploadQueueService _uploadService;
        private readonly IConfigurationService _configService;
        private readonly ILogger<FileMonitoringTestController> _logger;

        public FileMonitoringTestController(
            IUploadQueueService uploadService,
            IConfigurationService configService,
            ILogger<FileMonitoringTestController> logger)
        {
            _uploadService = uploadService;
            _configService = configService;
            _logger = logger;
        }

        [HttpPost("create-sample-files")]
        public async Task<IActionResult> CreateSampleFiles([FromBody] CreateSampleFilesRequest request)
        {
            try
            {
                var targetDirectory = request.Directory ?? await _configService.GetValueAsync("Monitoring.FolderPath") ?? "./test-data";

                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                var createdFiles = new List<string>();

                // Create sample JSON files
                for (int i = 1; i <= request.JsonFileCount; i++)
                {
                    var sampleData = new
                    {
                        id = Guid.NewGuid().ToString(),
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        deviceId = $"device_{i:D3}",
                        temperature = Math.Round(20 + (new Random().NextDouble() * 15), 2),
                        humidity = Math.Round(40 + (new Random().NextDouble() * 30), 2),
                        location = new { lat = -36.8485 + (new Random().NextDouble() * 0.1), lng = 174.7633 + (new Random().NextDouble() * 0.1) },
                        readings = new
                        {
                            pressure = Math.Round(1000 + (new Random().NextDouble() * 50), 2),
                            windSpeed = Math.Round(new Random().NextDouble() * 25, 1),
                            visibility = Math.Round(5 + (new Random().NextDouble() * 10), 1)
                        }
                    };

                    var fileName = $"sample_data_{DateTime.Now:yyyyMMdd_HHmmss}_{i:D3}.json";
                    var filePath = Path.Combine(targetDirectory, fileName);

                    var jsonString = JsonSerializer.Serialize(sampleData, new JsonSerializerOptions { WriteIndented = true });
                    await System.IO.File.WriteAllTextAsync(filePath, jsonString);

                    createdFiles.Add(fileName);

                    // Small delay to ensure unique timestamps
                    await Task.Delay(100);
                }

                // Create sample image placeholder files
                for (int i = 1; i <= request.ImageFileCount; i++)
                {
                    var fileName = $"sample_image_{DateTime.Now:yyyyMMdd_HHmmss}_{i:D3}.jpg";
                    var filePath = Path.Combine(targetDirectory, fileName);

                    // Create a simple text file as image placeholder
                    var imageMetadata = $"SAMPLE_IMAGE_FILE\nCreated: {DateTime.UtcNow}\nSize: 1920x1080\nDevice: camera_{i:D3}";
                    await System.IO.File.WriteAllTextAsync(filePath, imageMetadata);

                    createdFiles.Add(fileName);
                    await Task.Delay(50);
                }

                _logger.LogInformation("Created {Count} sample files in {Directory}", createdFiles.Count, targetDirectory);

                return Ok(new
                {
                    Message = $"Created {createdFiles.Count} sample files successfully",
                    Directory = targetDirectory,
                    Files = createdFiles,
                    JsonFiles = request.JsonFileCount,
                    ImageFiles = request.ImageFileCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create sample files");
                return StatusCode(500, new { Error = "Failed to create sample files", Details = ex.Message });
            }
        }

        [HttpPost("simulate-api-data")]
        public async Task<IActionResult> SimulateApiData([FromBody] SimulateApiDataRequest request)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "azure-gateway", "api-simulation");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                var processedFiles = new List<string>();

                for (int i = 1; i <= request.RecordCount; i++)
                {
                    ApiData apiData = request.DataType.ToLower() switch
                    {
                        "weather" => new ApiData
                        {
                            Id = $"weather_station_{i:D3}",
                            Timestamp = DateTime.UtcNow.AddMinutes(-i),
                            Type = "weather",
                            Data = new
                            {
                                temperature = Math.Round(-5 + (new Random().NextDouble() * 35), 2),
                                humidity = Math.Round(30 + (new Random().NextDouble() * 40), 2),
                                pressure = Math.Round(995 + (new Random().NextDouble() * 30), 2),
                                windSpeed = Math.Round(new Random().NextDouble() * 30, 1),
                                windDirection = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[new Random().Next(8)],
                                conditions = new[] { "Clear", "Cloudy", "Partly Cloudy", "Rainy", "Stormy" }[new Random().Next(5)]
                            }
                        },
                        "sensor" => new ApiData
                        {
                            Id = $"sensor_{i:D4}",
                            Timestamp = DateTime.UtcNow.AddMinutes(-i * 2),
                            Type = "sensor",
                            Data = new
                            {
                                deviceType = new[] { "temperature", "motion", "light", "sound" }[new Random().Next(4)],
                                value = Math.Round(new Random().NextDouble() * 100, 3),
                                unit = new[] { "celsius", "percent", "lux", "decibels" }[new Random().Next(4)],
                                location = $"Building_A_Floor_{(i % 5) + 1}",
                                status = new[] { "active", "inactive", "maintenance" }[new Random().Next(3)]
                            }
                        },
                        _ => new ApiData
                        {
                            Id = Guid.NewGuid().ToString(),
                            Timestamp = DateTime.UtcNow.AddMinutes(-i),
                            Type = request.DataType,
                            Data = $"Sample data record {i}"
                        }
                    };

                    var fileName = $"api_data_{request.DataType}_{DateTime.Now:yyyyMMdd_HHmmss}_{i:D4}.json";
                    var filePath = Path.Combine(tempDir, fileName);

                    var jsonString = JsonSerializer.Serialize(apiData, new JsonSerializerOptions { WriteIndented = true });
                    await System.IO.File.WriteAllTextAsync(filePath, jsonString);

                    // Simulate adding to upload queue (as if from API poller)
                    var hash = await FileHelper.CalculateFileHashAsync(filePath);
                    var fileSize = new FileInfo(filePath).Length;

                    var upload = await _uploadService.AddToQueueAsync(
                        filePath,
                        FileType.Json,
                        DataSource.Api,
                        fileSize,
                        hash);

                    processedFiles.Add($"{fileName} (Upload ID: {upload.Id})");
                    await Task.Delay(50);
                }

                _logger.LogInformation("Simulated {Count} API data records of type {Type}", request.RecordCount, request.DataType);

                return Ok(new
                {
                    Message = $"Successfully simulated {request.RecordCount} API data records",
                    DataType = request.DataType,
                    TempDirectory = tempDir,
                    ProcessedFiles = processedFiles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to simulate API data");
                return StatusCode(500, new { Error = "Failed to simulate API data", Details = ex.Message });
            }
        }

        [HttpGet("validate-setup")]
        public async Task<IActionResult> ValidateSetup()
        {
            try
            {
                var validation = new
                {
                    Timestamp = DateTime.UtcNow,
                    DatabaseConnection = false,
                    ConfigurationCount = 0,
                    MonitoringPaths = new List<string>(),
                    TempDirectories = new List<object>(),
                    Issues = new List<string>()
                };

                var issues = new List<string>();

                // Test database connection
                try
                {
                    var configs = await _configService.GetAllAsync();
                    validation = validation with
                    {
                        DatabaseConnection = true,
                        ConfigurationCount = configs.Count()
                    };
                }
                catch (Exception ex)
                {
                    issues.Add($"Database connection failed: {ex.Message}");
                }

                // Check monitoring paths
                var monitoringPaths = new List<string>();
                var folderPath = await _configService.GetValueAsync("Monitoring.FolderPath");
                var archivePath = await _configService.GetValueAsync("Monitoring.ArchivePath");
                var tempPath = await _configService.GetValueAsync("Api.TempDirectory");

                if (!string.IsNullOrEmpty(folderPath))
                {
                    monitoringPaths.Add(folderPath);
                    if (!Directory.Exists(folderPath))
                    {
                        issues.Add($"Monitoring folder does not exist: {folderPath}");
                    }
                }

                // Check temp directories
                var tempDirs = new List<object>();
                foreach (var path in new[] { archivePath, tempPath })
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        var exists = Directory.Exists(path);
                        tempDirs.Add(new { Path = path, Exists = exists });

                        if (!exists)
                        {
                            try
                            {
                                Directory.CreateDirectory(path);
                                tempDirs[^1] = new { Path = path, Exists = true, Created = true };
                            }
                            catch (Exception ex)
                            {
                                issues.Add($"Cannot create directory {path}: {ex.Message}");
                            }
                        }
                    }
                }

                var finalValidation = validation with
                {
                    MonitoringPaths = monitoringPaths,
                    TempDirectories = tempDirs,
                    Issues = issues
                };

                var isValid = issues.Count == 0;
                var statusCode = isValid ? 200 : 400;

                return StatusCode(statusCode, new
                {
                    IsValid = isValid,
                    ValidationResult = finalValidation,
                    Summary = new
                    {
                        Status = isValid ? "Setup is valid" : "Issues found in setup",
                        IssueCount = issues.Count,
                        ConfiguredPaths = monitoringPaths.Count,
                        DatabaseConnected = validation.DatabaseConnection
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Setup validation failed");
                return StatusCode(500, new { Error = "Setup validation failed", Details = ex.Message });
            }
        }
    }

    public class CreateSampleFilesRequest
    {
        public string? Directory { get; set; }
        public int JsonFileCount { get; set; } = 3;
        public int ImageFileCount { get; set; } = 2;
    }

    public class SimulateApiDataRequest
    {
        public string DataType { get; set; } = "sensor";
        public int RecordCount { get; set; } = 5;
    }
}