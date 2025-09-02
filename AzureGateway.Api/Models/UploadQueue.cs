using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AzureGateway.Api.Models
{
    public class UploadQueue
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;
        
        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;
        
        public FileType FileType { get; set; }
        
        public FileStatus Status { get; set; } = FileStatus.Pending;
        
        public DataSource Source { get; set; }
        
        public long FileSizeBytes { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastAttemptAt { get; set; }
        
        public int AttemptCount { get; set; } = 0;
        
        public int MaxRetries { get; set; } = 5;
        
        [StringLength(1000)]
        public string? ErrorMessage { get; set; }
        
        [StringLength(500)]
        public string? AzureBlobUrl { get; set; }
        
        [StringLength(100)]
        public string? AzureContainer { get; set; }
        
        [StringLength(255)]
        public string? AzureBlobName { get; set; }
        
        public DateTime? CompletedAt { get; set; }
        
        public long? UploadDurationMs { get; set; }
        
        [StringLength(50)]
        public string? Hash { get; set; } // For duplicate detection
        
        // Navigation property for upload progress
        public virtual ICollection<UploadProgress> UploadProgresses { get; set; } = new List<UploadProgress>();
    }

}