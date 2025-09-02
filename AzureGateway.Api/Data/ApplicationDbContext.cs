using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Models;

namespace AzureGateway.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<UploadQueue> UploadQueue { get; set; }
        public DbSet<UploadProgress> UploadProgress { get; set; }
        public DbSet<Configuration> Configuration { get; set; }
        public DbSet<UploadHistory> UploadHistory { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<DataSourceConfig> DataSourceConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure indexes for performance
            modelBuilder.Entity<UploadQueue>()
                .HasIndex(u => u.Status)
                .HasDatabaseName("IX_UploadQueue_Status");

            modelBuilder.Entity<UploadQueue>()
                .HasIndex(u => u.CreatedAt)
                .HasDatabaseName("IX_UploadQueue_CreatedAt");

            modelBuilder.Entity<UploadQueue>()
                .HasIndex(u => u.Hash)
                .HasDatabaseName("IX_UploadQueue_Hash");

            modelBuilder.Entity<UploadHistory>()
                .HasIndex(u => u.CompletedAt)
                .HasDatabaseName("IX_UploadHistory_CompletedAt");

            modelBuilder.Entity<SystemLog>()
                .HasIndex(l => l.Timestamp)
                .HasDatabaseName("IX_SystemLog_Timestamp");

            modelBuilder.Entity<SystemLog>()
                .HasIndex(l => l.Level)
                .HasDatabaseName("IX_SystemLog_Level");

            modelBuilder.Entity<DataSourceConfig>()
                .HasIndex(d => d.IsEnabled)
                .HasDatabaseName("IX_DataSourceConfig_IsEnabled");

            // Configure relationships
            modelBuilder.Entity<UploadProgress>()
                .HasOne(up => up.UploadQueue)
                .WithMany(uq => uq.UploadProgresses)
                .HasForeignKey(up => up.UploadQueueId)
                .OnDelete(DeleteBehavior.Cascade);

            // Seed default configurations
            SeedDefaultData(modelBuilder);
        }

        private static void SeedDefaultData(ModelBuilder modelBuilder)
        {
            var defaultConfigs = new[]
            {
                new Configuration
                {
                    Key = "Azure.StorageConnectionString",
                    Value = "",
                    Description = "Azure Storage Account connection string",
                    Category = "Azure",
                    IsEncrypted = true
                },
                new Configuration
                {
                    Key = "Azure.DefaultContainer",
                    Value = "gateway-data",
                    Description = "Default Azure blob container name",
                    Category = "Azure"
                },
                new Configuration
                {
                    Key = "Upload.MaxRetries",
                    Value = "5",
                    Description = "Maximum number of retry attempts for failed uploads",
                    Category = "Upload"
                },
                new Configuration
                {
                    Key = "Upload.RetryDelaySeconds",
                    Value = "30",
                    Description = "Delay in seconds between retry attempts",
                    Category = "Upload"
                },
                new Configuration
                {
                    Key = "Upload.BatchSize",
                    Value = "10",
                    Description = "Number of files to process in each batch",
                    Category = "Upload"
                },
                new Configuration
                {
                    Key = "Monitoring.FolderPath",
                    Value = "/home/pi/gateway/incoming",
                    Description = "Default folder path to monitor for new files",
                    Category = "Monitoring"
                },
                new Configuration
                {
                    Key = "Api.PollingIntervalMinutes",
                    Value = "5",
                    Description = "Interval in minutes for polling third-party API",
                    Category = "Api"
                }
            };

            modelBuilder.Entity<Configuration>().HasData(defaultConfigs);
        }
    }
}