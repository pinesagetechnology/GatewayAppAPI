using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AzureGateway.Api.Models
{
    public class UploadHistory
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;
        
        public FileType FileType { get; set; }
        
        public DataSource Source { get; set; }
        
        public FileStatus FinalStatus { get; set; }
        
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
        
        public long FileSizeBytes { get; set; }
        
        public long? UploadDurationMs { get; set; }
        
        public int TotalAttempts { get; set; }
        
        [StringLength(500)]
        public string? AzureBlobUrl { get; set; }
        
        [StringLength(1000)]
        public string? FinalErrorMessage { get; set; }
    }
}