using AzureGateway.Api.Data;
using AzureGateway.Api.Models;
using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzureGateway.APi.Tests.Services
{
    public class DatabaseHealthServiceTests
    {
        private ApplicationDbContext BuildContextWithData(int pending = 0, int processing = 0, int completed = 0, int failed = 0)
        {
            var ctx = TestDbContextFactory.CreateInMemory();
            for (int i = 0; i < pending; i++) ctx.UploadQueue.Add(new UploadQueue { FileName = $"p{i}", Status = FileStatus.Pending, CreatedAt = DateTime.UtcNow.AddMinutes(-i) });
            for (int i = 0; i < processing; i++) ctx.UploadQueue.Add(new UploadQueue { FileName = $"r{i}", Status = FileStatus.Processing, CreatedAt = DateTime.UtcNow.AddMinutes(-i) });
            for (int i = 0; i < completed; i++) ctx.UploadQueue.Add(new UploadQueue { FileName = $"c{i}", Status = FileStatus.Completed, CreatedAt = DateTime.UtcNow.AddMinutes(-i) });
            for (int i = 0; i < failed; i++) ctx.UploadQueue.Add(new UploadQueue { FileName = $"f{i}", Status = FileStatus.Failed, CreatedAt = DateTime.UtcNow.AddMinutes(-i) });
            ctx.SaveChanges();
            return ctx;
        }

        [Fact]
        public async Task CanConnectAsync_ReturnsTrue_WithInMemoryDb()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            var uploadSvc = new Mock<IUploadQueueService>();
            var configSvc = new Mock<IConfigurationService>();
            var svc = new DatabaseHealthService(ctx, uploadSvc.Object, configSvc.Object, new NullLogger<DatabaseHealthService>());

            var can = await svc.CanConnectAsync();
            can.Should().BeTrue();
        }

        [Fact]
        public async Task GetDatabaseStatsAsync_ReturnsCounts()
        {
            using var ctx = BuildContextWithData(pending: 2, processing: 3, completed: 4, failed: 5);
            var uploadSvc = new Mock<IUploadQueueService>();
            var configSvc = new Mock<IConfigurationService>();
            var svc = new DatabaseHealthService(ctx, uploadSvc.Object, configSvc.Object, new NullLogger<DatabaseHealthService>());

            var stats = await svc.GetDatabaseStatsAsync();
            stats["PendingUploads"].Should().Be(2);
            stats["ProcessingUploads"].Should().Be(3);
            stats["CompletedUploads"].Should().Be(4);
            stats["FailedUploads"].Should().Be(5);
        }

        [Fact]
        public async Task CheckHealthAsync_ComposesIssues_WhenThresholdsExceeded()
        {
            using var ctx = BuildContextWithData(pending: 1001, failed: 101);
            var uploadSvc = new Mock<IUploadQueueService>();
            var configSvc = new Mock<IConfigurationService>();
            var svc = new DatabaseHealthService(ctx, uploadSvc.Object, configSvc.Object, new NullLogger<DatabaseHealthService>());

            var health = await svc.CheckHealthAsync();
            health.IsHealthy.Should().BeFalse();
            health.Issues.Should().NotBeEmpty();
            health.Stats.Should().NotBeNull();
        }
    }
}
