using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AzureGateway.Api.Models
{
    public class DataSourceConfig
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        public DataSource SourceType { get; set; }
        
        public bool IsEnabled { get; set; } = true;
        
        [StringLength(500)]
        public string? FolderPath { get; set; }
        
        [StringLength(500)]
        public string? ApiEndpoint { get; set; }
        
        [StringLength(200)]
        public string? ApiKey { get; set; }
        
        public int PollingIntervalMinutes { get; set; } = 5;
        
        [StringLength(100)]
        public string? FilePattern { get; set; } = "*.*";
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastProcessedAt { get; set; }
        
        [StringLength(1000)]
        public string? AdditionalSettings { get; set; } // JSON for extra configs
    }

    public class DataSourceStatus
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DataSource Type { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastActivity { get; set; }
        public long FilesProcessed { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastErrorAt { get; set; }
    }
}
