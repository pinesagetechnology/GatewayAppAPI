// API polling disabled - commenting out entire test file
/*
using Moq;
using AzureGateway.Api.Models;
using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AzureGateway.APi.Tests.Services
{
    public class ApiPollerTests
    {
        private DataSourceConfig CreateTestConfig(string name = "Test API", string endpoint = "https://api.test.com/data")
        {
            return new DataSourceConfig
            {
                Id = 1,
                Name = name,
                SourceType = DataSource.Api,
                ApiEndpoint = endpoint,
                ApiKey = "test-key",
                PollingIntervalMinutes = 5,
                AdditionalSettings = "{\"Headers\":{\"X-Custom\":\"value\"}}"
            };
        }

        [Fact]
        public async Task StartAsync_Throws_WhenApiEndpointMissing()
        {
            var config = CreateTestConfig(endpoint: "");
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<ApiPoller>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<ApiPoller>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var poller = new ApiPoller(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() => poller.StartAsync());
            onError.Verify(e => e(config.Id, "API endpoint is not configured"), Times.Once);
        }

        [Fact]
        public async Task StartAsync_StartsTimer_WhenEndpointValid()
        {
            var config = CreateTestConfig();
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<ApiPoller>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<ApiPoller>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var poller = new ApiPoller(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await poller.StartAsync();
            poller.IsRunning.Should().BeTrue();
        }

        [Fact]
        public async Task StopAsync_StopsTimer_AndCleansUp()
        {
            var config = CreateTestConfig();
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<ApiPoller>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<ApiPoller>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var poller = new ApiPoller(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await poller.StartAsync();
            poller.IsRunning.Should().BeTrue();
            
            await poller.StopAsync();
            poller.IsRunning.Should().BeFalse();
        }

        [Fact]
        public async Task StartAsync_IsIdempotent()
        {
            var config = CreateTestConfig();
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<ApiPoller>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<ApiPoller>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var poller = new ApiPoller(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await poller.StartAsync();
            await poller.StartAsync(); // Should not throw or cause issues
            
            poller.IsRunning.Should().BeTrue();
        }

        [Fact]
        public async Task StopAsync_IsIdempotent()
        {
            var config = CreateTestConfig();
            var serviceProvider = new Mock<IServiceProvider>();
            var logger = new Mock<ILogger<ApiPoller>>();
            var configService = new Mock<IConfigurationService>();
            
            serviceProvider.Setup(sp => sp.GetService(typeof(ILogger<ApiPoller>))).Returns(logger.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService))).Returns(configService.Object);
            
            var onFileProcessed = new Mock<Func<int, string, Task>>();
            var onError = new Mock<Func<int, string, Task>>();
            
            var poller = new ApiPoller(config, serviceProvider.Object, onFileProcessed.Object, onError.Object);

            await poller.StartAsync();
            await poller.StopAsync();
            await poller.StopAsync(); // Should not throw
            
            poller.IsRunning.Should().BeFalse();
        }
    }
}
*/
