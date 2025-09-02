using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Services;
using AzureGateway.Api.Data.Repository;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection not found");

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(connectionString);
                options.EnableSensitiveDataLogging(false);
                options.EnableDetailedErrors(true);
            });

            // Register repositories
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            
            // Register services
            services.AddScoped<IUploadQueueService, UploadQueueService>();
            services.AddScoped<IConfigurationService, ConfigurationService>();

            return services;
        }

        public static async Task<IServiceProvider> InitializeDatabaseAsync(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

            await DatabaseInitializer.InitializeAsync(context, logger);
            return services;
        }
    }
}
