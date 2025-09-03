using AzureGateway.Api.Data;
using AzureGateway.Api.Services;
using FluentAssertions;

namespace AzureGateway.APi.Tests.Services
{
    public class ConfigurationServiceTests
    {
        [Fact]
        public async Task GetValueAsync_ReturnsValue_WhenKeyExists()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            ctx.Configuration.Add(new AzureGateway.Api.Models.Configuration { Key = "Test.Key", Value = "abc" });
            await ctx.SaveChangesAsync();

            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationService>();
            var svc = new ConfigurationService(ctx, logger);

            var value = await svc.GetValueAsync("Test.Key");
            value.Should().Be("abc");
        }

        [Fact]
        public async Task GetValueAsync_Typed_ParsesIntBoolDouble()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            ctx.Configuration.AddRange(
                new AzureGateway.Api.Models.Configuration { Key = "IntKey", Value = "42" },
                new AzureGateway.Api.Models.Configuration { Key = "BoolKey", Value = "true" },
                new AzureGateway.Api.Models.Configuration { Key = "DoubleKey", Value = "3.14" }
            );
            await ctx.SaveChangesAsync();

            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationService>();
            var svc = new ConfigurationService(ctx, logger);

            (await svc.GetValueAsync<int>("IntKey")).Should().Be(42);
            (await svc.GetValueAsync<bool>("BoolKey")).Should().BeTrue();
            (await svc.GetValueAsync<double>("DoubleKey")).Should().Be(3.14);
        }

        [Fact]
        public async Task SetValueAsync_InsertsAndUpdates()
        {
            using var ctx = TestDbContextFactory.CreateInMemory();
            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigurationService>();
            var svc = new ConfigurationService(ctx, logger);

            await svc.SetValueAsync("A", "1", "d", "c");
            (await svc.GetValueAsync("A")).Should().Be("1");

            await svc.SetValueAsync("A", "2");
            (await svc.GetValueAsync("A")).Should().Be("2");
        }
    }
}


