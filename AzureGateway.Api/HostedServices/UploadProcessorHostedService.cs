using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.HostedServices
{
    public class UploadProcessorHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<UploadProcessorHostedService> _logger;

        public UploadProcessorHostedService(
            IServiceProvider serviceProvider,
            ILogger<UploadProcessorHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Upload Processor Hosted Service starting...");

            // Wait for application to fully start
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var uploadProcessor = scope.ServiceProvider.GetRequiredService<IUploadProcessorService>();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

                // Check if auto-start is enabled
                var autoStart = await configService.GetValueAsync<bool?>("Upload.AutoStart") ?? true;

                if (autoStart)
                {
                    _logger.LogInformation("Auto-starting upload processor...");
                    await uploadProcessor.StartAsync(stoppingToken);
                }
                else
                {
                    _logger.LogInformation("Upload processor auto-start is disabled");
                }

                // Keep the service running
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Upload Processor Hosted Service stopping...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload Processor Hosted Service encountered an error");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Upload Processor Hosted Service stopping...");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var uploadProcessor = scope.ServiceProvider.GetRequiredService<IUploadProcessorService>();

                if (await uploadProcessor.IsRunningAsync())
                {
                    await uploadProcessor.StopAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping upload processor");
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Upload Processor Hosted Service stopped");
        }
    }
}
