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
        }
    }
}