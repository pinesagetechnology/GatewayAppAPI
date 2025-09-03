using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzureGateway.APi.Tests.Services
{
    public class AzureStorageServiceTests
    {
        [Fact]
        public async Task IsConnectedAsync_ReturnsFalse_WhenClientNotInitialized()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync("Azure.StorageConnectionString"))
                  .ReturnsAsync((string?)null);
            var logger = new NullLogger<AzureStorageService>();
            var svc = new AzureStorageService(config.Object, logger);

            var isConnected = await svc.IsConnectedAsync();
            isConnected.Should().BeFalse();
        }

        [Fact]
        public async Task CreateContainerIfNotExistsAsync_ReturnsFalse_WhenClientNotInitialized()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync("Azure.StorageConnectionString"))
                  .ReturnsAsync((string?)null);
            var logger = new NullLogger<AzureStorageService>();
            var svc = new AzureStorageService(config.Object, logger);

            var result = await svc.CreateContainerIfNotExistsAsync("test-container");
            result.Should().BeFalse();
        }

        [Fact]
        public async Task UploadDataAsync_ReturnsError_WhenClientNotInitialized()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync("Azure.StorageConnectionString"))
                  .ReturnsAsync((string?)null);
            var logger = new NullLogger<AzureStorageService>();
            var svc = new AzureStorageService(config.Object, logger);

            var result = await svc.UploadDataAsync(new byte[] { 1, 2, 3 }, "test.txt", "test-container");
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Azure Storage client is not initialized");
        }

        [Fact]
        public async Task GetStorageInfoAsync_ReturnsError_WhenClientNotInitialized()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync("Azure.StorageConnectionString"))
                  .ReturnsAsync((string?)null);
            var logger = new NullLogger<AzureStorageService>();
            var svc = new AzureStorageService(config.Object, logger);

            var info = await svc.GetStorageInfoAsync();
            info.IsConnected.Should().BeFalse();
            info.ErrorMessage.Should().Be("Azure Storage client is not initialized");
        }

        [Fact]
        public async Task ListBlobsAsync_ReturnsEmpty_WhenClientNotInitialized()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync("Azure.StorageConnectionString"))
                  .ReturnsAsync((string?)null);
            var logger = new NullLogger<AzureStorageService>();
            var svc = new AzureStorageService(config.Object, logger);

            var blobs = await svc.ListBlobsAsync("test-container");
            blobs.Should().BeEmpty();
        }

        [Fact]
        public async Task DeleteBlobAsync_ReturnsFalse_WhenClientNotInitialized()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync("Azure.StorageConnectionString"))
                  .ReturnsAsync((string?)null);
            var logger = new NullLogger<AzureStorageService>();
            var svc = new AzureStorageService(config.Object, logger);

            var result = await svc.DeleteBlobAsync("test-container", "test-blob");
            result.Should().BeFalse();
        }
    }
}
