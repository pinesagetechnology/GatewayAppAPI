using AzureGateway.Api.Data;
using AzureGateway.Api.Hubs;
using AzureGateway.Api.Models;
using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzureGateway.APi.Tests.Services
{
    public class UploadProcessorServiceTests
    {
        private ServiceProvider BuildServiceProvider(ApplicationDbContext ctx,
            Mock<IUploadQueueService> uploadQueueMock,
            Mock<IAzureStorageService> azureMock,
            Mock<IConfigurationService> configMock)
        {
            var services = new ServiceCollection();
            services.AddSingleton(ctx);
            services.AddSingleton<IUploadQueueService>(uploadQueueMock.Object);
            services.AddSingleton<IAzureStorageService>(azureMock.Object);
            services.AddSingleton<IConfigurationService>(configMock.Object);
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task ProcessPendingUploadsAsync_Skips_WhenAzureNotConnected()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            var hub = new Mock<IHubContext<UploadStatusHub>>();
            var uploadQueue = new Mock<IUploadQueueService>();
            uploadQueue.Setup(u => u.GetPendingUploadsAsync()).ReturnsAsync(Array.Empty<UploadQueue>());
            var azure = new Mock<IAzureStorageService>();
            azure.Setup(a => a.IsConnectedAsync()).ReturnsAsync(false);
            var config = new Mock<IConfigurationService>();

            var sp = BuildServiceProvider(ctx, uploadQueue, azure, config);
            var svc = new UploadProcessorService(sp, hub.Object, new NullLogger<UploadProcessorService>());

            await svc.StartAsync(CancellationToken.None);
            await svc.ProcessPendingUploadsAsync(maxConcurrent: 1);

            uploadQueue.Verify(u => u.GetPendingUploadsAsync(), Times.Never);
        }

        [Fact]
        public async Task StartAndStop_TogglesRunningState()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            var hub = new Mock<IHubContext<UploadStatusHub>>();
            var uploadQueue = new Mock<IUploadQueueService>();
            var azure = new Mock<IAzureStorageService>();
            azure.Setup(a => a.IsConnectedAsync()).ReturnsAsync(true);
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync<int?>("Azure.MaxConcurrentUploads")).ReturnsAsync(1);
            config.Setup(c => c.GetValueAsync("Azure.DefaultContainer")).ReturnsAsync("gateway-data");

            var sp = BuildServiceProvider(ctx, uploadQueue, azure, config);
            var svc = new UploadProcessorService(sp, hub.Object, new NullLogger<UploadProcessorService>());

            await svc.StartAsync(CancellationToken.None);
            (await svc.IsRunningAsync()).Should().BeTrue();
            await svc.StopAsync(CancellationToken.None);
            (await svc.IsRunningAsync()).Should().BeFalse();
        }
    }
}


