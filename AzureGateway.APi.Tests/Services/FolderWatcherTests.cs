using AzureGateway.Api.Models;
using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzureGateway.APi.Tests.Services
{
    public class FolderWatcherTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        private DataSourceConfig CreateTestConfig(string folderPath)
        {
            return new DataSourceConfig
            {
                Id = 1,
                Name = "Test Folder",
                SourceType = DataSource.Folder,
                FolderPath = folderPath,
                FilePattern = "*.txt",
                IsEnabled = true
            };
        }

        private string CreateTempDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"folderwatcher-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            _tempDirs.Add(tempDir);
            return tempDir;
        }

        [Fact]
        public async Task StartAsync_Throws_WhenFolderDoesNotExist()
        {
            var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid()}");
            var config = CreateTestConfig(nonExistentPath);
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<FolderWatcher>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<FolderWatcher>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var watcher = new FolderWatcher(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => watcher.StartAsync());
            onError.Verify(e => e(config.Id, It.Is<string>(s => s.Contains("does not exist"))), Times.Once);
        }

        [Fact]
        public async Task StartAsync_StartsMonitoring_WhenFolderExists()
        {
            var tempDir = CreateTempDirectory();
            var config = CreateTestConfig(tempDir);
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<FolderWatcher>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<FolderWatcher>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var watcher = new FolderWatcher(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await watcher.StartAsync();
            watcher.IsRunning.Should().BeTrue();
        }

        [Fact]
        public async Task StopAsync_StopsMonitoring_AndCleansUp()
        {
            var tempDir = CreateTempDirectory();
            var config = CreateTestConfig(tempDir);
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<FolderWatcher>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<FolderWatcher>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var watcher = new FolderWatcher(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await watcher.StartAsync();
            watcher.IsRunning.Should().BeTrue();
            
            await watcher.StopAsync();
            watcher.IsRunning.Should().BeFalse();
        }

        [Fact]
        public async Task StartAsync_IsIdempotent()
        {
            var tempDir = CreateTempDirectory();
            var config = CreateTestConfig(tempDir);
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<FolderWatcher>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<FolderWatcher>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var watcher = new FolderWatcher(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await watcher.StartAsync();
            await watcher.StartAsync(); // Should not throw or cause issues
            
            watcher.IsRunning.Should().BeTrue();
        }

        [Fact]
        public async Task StopAsync_IsIdempotent()
        {
            var tempDir = CreateTempDirectory();
            var config = CreateTestConfig(tempDir);
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<FolderWatcher>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<FolderWatcher>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var watcher = new FolderWatcher(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await watcher.StartAsync();
            await watcher.StopAsync();
            await watcher.StopAsync(); // Should not throw
            
            watcher.IsRunning.Should().BeFalse();
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
