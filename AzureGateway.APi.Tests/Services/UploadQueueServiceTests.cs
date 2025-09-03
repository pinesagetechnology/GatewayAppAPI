using AzureGateway.Api.Models;
using AzureGateway.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureGateway.APi.Tests.Services
{
    public class UploadQueueServiceTests
    {
        [Fact]
        public async Task AddToQueueAsync_AddsItem_AndPreventsDuplicateByHash()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            var svc = new UploadQueueService(ctx, new NullLogger<UploadQueueService>());

            var first = await svc.AddToQueueAsync("/a/b/file1.json", FileType.Json, DataSource.Folder, 100, hash: "h1");
            var second = await svc.AddToQueueAsync("/a/b/file1.json", FileType.Json, DataSource.Folder, 100, hash: "h1");

            first.Id.Should().Be(second.Id);
            (await svc.GetPendingUploadsAsync()).Should().HaveCount(1);
        }

        [Fact]
        public async Task UpdateStatusAsync_Completes_SetsCompletedAt()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            var svc = new UploadQueueService(ctx, new NullLogger<UploadQueueService>());

            var item = await svc.AddToQueueAsync("/a/b/file2.json", FileType.Json, DataSource.Api, 200);
            await svc.UpdateStatusAsync(item.Id, FileStatus.Completed);

            var updated = (await svc.GetPendingUploadsAsync()).FirstOrDefault(u => u.Id == item.Id);
            updated.Should().BeNull();
            var all = ctx.UploadQueue.ToList();
            all.First(u => u.Id == item.Id).CompletedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task UpdateProgressAsync_CreatesAndUpdates()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            var svc = new UploadQueueService(ctx, new NullLogger<UploadQueueService>());

            var item = await svc.AddToQueueAsync("/a/b/file3.bin", FileType.Image, DataSource.Folder, 1000);
            await svc.UpdateProgressAsync(item.Id, 100, 1000, "starting");
            await svc.UpdateProgressAsync(item.Id, 500, 1000, "half");

            var p = ctx.UploadProgress.Single(x => x.UploadQueueId == item.Id);
            p.BytesUploaded.Should().Be(500);
            p.TotalBytes.Should().Be(1000);
            p.StatusMessage.Should().Be("half");
        }
    }
}


