using AzureGateway.Api.HostedServices;
using AzureGateway.Api.Services.interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AzureGateway.APi.Tests.Services
{
    public class FileMonitoringHostedServiceTests
    {
        [Fact]
        public async Task ExecuteAsync_StartsMonitoring_WhenAutoStartEnabled()
        {
            var serviceProvider = new Mock<IServiceProvider>();
            var monitoringService = new Mock<IFileMonitoringService>();
            var configService = new Mock<IConfigurationService>();
            var logger = new NullLogger<FileMonitoringHostedService>();

            var scope = new Mock<IServiceScope>();
            var scopeFactory = new Mock<IServiceScopeFactory>();
            
            scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
            scopeFactory.Setup(sf => sf.CreateScope()).Returns(scope.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
            serviceProvider.Setup(sp => sp.GetRequiredService<IFileMonitoringService>()).Returns(monitoringService.Object);
            serviceProvider.Setup(sp => sp.GetRequiredService<IConfigurationService>()).Returns(configService.Object);
            
            configService.Setup(c => c.GetValueAsync<bool?>("FileMonitoring.AutoStart")).ReturnsAsync(true);
            monitoringService.Setup(m => m.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var hostedService = new FileMonitoringHostedService(serviceProvider.Object, logger);

            // Start the service
            await hostedService.StartAsync(CancellationToken.None);

            // Wait a bit for ExecuteAsync to run
            await Task.Delay(100);

            monitoringService.Verify(m => m.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_DoesNotStartMonitoring_WhenAutoStartDisabled()
        {
            var serviceProvider = new Mock<IServiceProvider>();
            var monitoringService = new Mock<IFileMonitoringService>();
            var configService = new Mock<IConfigurationService>();
            var logger = new NullLogger<FileMonitoringHostedService>();

            var scope = new Mock<IServiceScope>();
            var scopeFactory = new Mock<IServiceScopeFactory>();
            
            scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
            scopeFactory.Setup(sf => sf.CreateScope()).Returns(scope.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
            serviceProvider.Setup(sp => sp.GetRequiredService<IFileMonitoringService>()).Returns(monitoringService.Object);
            serviceProvider.Setup(sp => sp.GetRequiredService<IConfigurationService>()).Returns(configService.Object);
            
            configService.Setup(c => c.GetValueAsync<bool?>("FileMonitoring.AutoStart")).ReturnsAsync(false);

            var hostedService = new FileMonitoringHostedService(serviceProvider.Object, logger);

            // Start the service
            await hostedService.StartAsync(CancellationToken.None);

            // Wait a bit for ExecuteAsync to run
            await Task.Delay(100);

            monitoringService.Verify(m => m.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task StopAsync_StopsMonitoring_WhenRunning()
        {
            var serviceProvider = new Mock<IServiceProvider>();
            var monitoringService = new Mock<IFileMonitoringService>();
            var logger = new NullLogger<FileMonitoringHostedService>();

            var scope = new Mock<IServiceScope>();
            var scopeFactory = new Mock<IServiceScopeFactory>();
            
            scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
            scopeFactory.Setup(sf => sf.CreateScope()).Returns(scope.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
            serviceProvider.Setup(sp => sp.GetRequiredService<IFileMonitoringService>()).Returns(monitoringService.Object);
            
            monitoringService.Setup(m => m.IsRunningAsync()).ReturnsAsync(true);
            monitoringService.Setup(m => m.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var hostedService = new FileMonitoringHostedService(serviceProvider.Object, logger);

            // Stop the service
            await hostedService.StopAsync(CancellationToken.None);

            monitoringService.Verify(m => m.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task StopAsync_DoesNotStopMonitoring_WhenNotRunning()
        {
            var serviceProvider = new Mock<IServiceProvider>();
            var monitoringService = new Mock<IFileMonitoringService>();
            var logger = new NullLogger<FileMonitoringHostedService>();

            var scope = new Mock<IServiceScope>();
            var scopeFactory = new Mock<IServiceScopeFactory>();
            
            scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
            scopeFactory.Setup(sf => sf.CreateScope()).Returns(scope.Object);
            serviceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
            serviceProvider.Setup(sp => sp.GetRequiredService<IFileMonitoringService>()).Returns(monitoringService.Object);
            
            monitoringService.Setup(m => m.IsRunningAsync()).ReturnsAsync(false);

            var hostedService = new FileMonitoringHostedService(serviceProvider.Object, logger);

            // Stop the service
            await hostedService.StopAsync(CancellationToken.None);

            monitoringService.Verify(m => m.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
