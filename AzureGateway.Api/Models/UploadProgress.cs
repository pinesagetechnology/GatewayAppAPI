using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AzureGateway.Api.Models
{
    public class UploadProgress
    {
        [Key]
        public int Id { get; set; }
        
        public int UploadQueueId { get; set; }
        
        [ForeignKey("UploadQueueId")]
        public virtual UploadQueue UploadQueue { get; set; } = null!;
        
        public long BytesUploaded { get; set; }
        
        public long TotalBytes { get; set; }
        
        public double PercentageComplete => TotalBytes > 0 ? (double)BytesUploaded / TotalBytes * 100 : 0;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [StringLength(200)]
        public string? StatusMessage { get; set; }
    }

}