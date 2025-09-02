using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AzureGateway.Api.Models
{
    public class SystemLog
    {
        [Key]
        public int Id { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        [StringLength(50)]
        public string Level { get; set; } = "Info"; // Info, Warning, Error, Debug
        
        [StringLength(100)]
        public string? Category { get; set; }
        
        [Required]
        public string Message { get; set; } = string.Empty;
        
        public string? Exception { get; set; }
        
        [StringLength(100)]
        public string? CorrelationId { get; set; }
    }
}
