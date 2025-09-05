using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.HostedServices
{
    public class ApiPollingHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ApiPollingHostedService> _logger;

        public ApiPollingHostedService(IServiceProvider serviceProvider, ILogger<ApiPollingHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("API Polling Hosted Service starting...");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var pollingService = scope.ServiceProvider.GetRequiredService<IApiPollingService>();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();

                var autoStart = await configService.GetValueAsync<bool?>("ApiPolling.AutoStart") ?? false;
                if (autoStart)
                {
                    _logger.LogInformation("Auto-starting API polling service...");
                    await pollingService.StartAsync(stoppingToken);
                }
                else
                {
                    _logger.LogInformation("API polling auto-start is disabled");
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("API Polling Hosted Service stopping...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API Polling Hosted Service encountered an error");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("API Polling Hosted Service stopping...");
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var pollingService = scope.ServiceProvider.GetRequiredService<IApiPollingService>();
                if (await pollingService.IsRunningAsync())
                {
                    await pollingService.StopAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping API polling service");
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("API Polling Hosted Service stopped");
        }
    }
}

