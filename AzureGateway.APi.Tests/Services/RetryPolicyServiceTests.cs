using AzureGateway.Api.Services;
using AzureGateway.Api.Services.interfaces;
using FluentAssertions;
using Moq;

namespace AzureGateway.APi.Tests.Services
{
    public class RetryPolicyServiceTests
    {
        [Fact]
        public async Task GetRetryDelayAsync_UsesExponentialBackoff_AndCaps()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync<int?>("Upload.RetryDelaySeconds"))
                  .ReturnsAsync(10);
            config.Setup(c => c.GetValueAsync<int?>("Upload.MaxRetryDelayMinutes"))
                  .ReturnsAsync(1); // 60s cap

            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<RetryPolicyService>();
            var svc = new RetryPolicyService(config.Object, logger);

            // attempt 1 -> 10s, attempt 2 -> 20s, attempt 4 -> 80s but capped to 60s
            (await svc.GetRetryDelayAsync(1)).Should().Be(TimeSpan.FromSeconds(10));
            (await svc.GetRetryDelayAsync(2)).Should().Be(TimeSpan.FromSeconds(20));
            (await svc.GetRetryDelayAsync(4)).Should().Be(TimeSpan.FromSeconds(60));
        }

        [Fact]
        public async Task ShouldRetryAsync_StopsOnMaxRetries_AndNonRetryableErrors()
        {
            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetValueAsync<int?>("Upload.MaxRetries"))
                  .ReturnsAsync(3);

            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<RetryPolicyService>();
            var svc = new RetryPolicyService(config.Object, logger);

            (await svc.ShouldRetryAsync(1, null)).Should().BeTrue();
            (await svc.ShouldRetryAsync(3, null)).Should().BeFalse();
            (await svc.ShouldRetryAsync(1, "File Not Found at path")).Should().BeFalse();
        }
    }
}


