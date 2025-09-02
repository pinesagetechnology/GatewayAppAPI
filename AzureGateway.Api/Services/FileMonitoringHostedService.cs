using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.HostedServices
{
    public class FileMonitoringHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FileMonitoringHostedService> _logger;

        public FileMonitoringHostedService(
            IServiceProvider serviceProvider,
            ILogger<FileMonitoringHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("File Monitoring Hosted Service starting...");

            // Wait a bit for the application to fully start
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var monitoringService = scope.ServiceProvider.GetRequiredService<IFileMonitoringService>();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

                // Check if auto-start is enabled
                var autoStart = await configService.GetValueAsync<bool?>("FileMonitoring.AutoStart") ?? true;

                if (autoStart)
                {
                    _logger.LogInformation("Auto-starting file monitoring service...");
                    await monitoringService.StartAsync(stoppingToken);
                }
                else
                {
                    _logger.LogInformation("File monitoring auto-start is disabled");
                }

                // Keep the service running
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("File Monitoring Hosted Service stopping...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File Monitoring Hosted Service encountered an error");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("File Monitoring Hosted Service stopping...");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var monitoringService = scope.ServiceProvider.GetRequiredService<IFileMonitoringService>();

                if (await monitoringService.IsRunningAsync())
                {
                    await monitoringService.StopAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping file monitoring service");
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("File Monitoring Hosted Service stopped");
        }
    }
}