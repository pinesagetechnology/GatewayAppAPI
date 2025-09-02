using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Models;
using AzureGateway.Api.Services.interfaces;

namespace AzureGateway.Api.Services
{
    public class UploadQueueService : IUploadQueueService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UploadQueueService> _logger;

        public UploadQueueService(ApplicationDbContext context, ILogger<UploadQueueService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<UploadQueue> AddToQueueAsync(string filePath, FileType fileType, DataSource source, long fileSize, string? hash = null)
        {
            // Check for duplicates if hash is provided
            if (!string.IsNullOrEmpty(hash) && await IsDuplicateAsync(hash))
            {
                _logger.LogInformation("Duplicate file detected with hash: {Hash}", hash);
                var existing = await _context.UploadQueue.FirstAsync(u => u.Hash == hash);
                return existing;
            }

            var fileName = Path.GetFileName(filePath);
            var uploadItem = new UploadQueue
            {
                FilePath = filePath,
                FileName = fileName,
                FileType = fileType,
                Source = source,
                FileSizeBytes = fileSize,
                Status = FileStatus.Pending,
                Hash = hash,
                CreatedAt = DateTime.UtcNow
            };

            _context.UploadQueue.Add(uploadItem);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Added file to upload queue: {FileName} (ID: {Id})", fileName, uploadItem.Id);
            return uploadItem;
        }

        public async Task<IEnumerable<UploadQueue>> GetPendingUploadsAsync()
        {
            return await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Pending)
                .OrderBy(u => u.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<UploadQueue>> GetFailedUploadsAsync()
        {
            return await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Failed)
                .OrderByDescending(u => u.LastAttemptAt)
                .ToListAsync();
        }

        public async Task<UploadQueue?> GetNextUploadAsync()
        {
            return await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Pending)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task UpdateStatusAsync(int id, FileStatus status, string? errorMessage = null)
        {
            var upload = await _context.UploadQueue.FindAsync(id);
            if (upload == null) return;

            upload.Status = status;
            upload.ErrorMessage = errorMessage;
            
            if (status == FileStatus.Completed)
            {
                upload.CompletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            _logger.LogDebug("Updated upload {Id} status to {Status}", id, status);
        }

        public async Task UpdateProgressAsync(int id, long bytesUploaded, long totalBytes, string? statusMessage = null)
        {
            // Update or create progress record
            var existingProgress = await _context.UploadProgress
                .FirstOrDefaultAsync(p => p.UploadQueueId == id);

            if (existingProgress != null)
            {
                existingProgress.BytesUploaded = bytesUploaded;
                existingProgress.TotalBytes = totalBytes;
                existingProgress.StatusMessage = statusMessage;
                existingProgress.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var progress = new UploadProgress
                {
                    UploadQueueId = id,
                    BytesUploaded = bytesUploaded,
                    TotalBytes = totalBytes,
                    StatusMessage = statusMessage
                };
                _context.UploadProgress.Add(progress);
            }

            await _context.SaveChangesAsync();
        }

        public async Task MarkAsCompletedAsync(int id, string azureBlobUrl, long uploadDurationMs)
        {
            var upload = await _context.UploadQueue.FindAsync(id);
            if (upload == null) return;

            upload.Status = FileStatus.Completed;
            upload.AzureBlobUrl = azureBlobUrl;
            upload.UploadDurationMs = uploadDurationMs;
            upload.CompletedAt = DateTime.UtcNow;

            // Archive to history
            var historyRecord = new UploadHistory
            {
                FileName = upload.FileName,
                FileType = upload.FileType,
                Source = upload.Source,
                FinalStatus = FileStatus.Completed,
                CompletedAt = DateTime.UtcNow,
                FileSizeBytes = upload.FileSizeBytes,
                UploadDurationMs = uploadDurationMs,
                TotalAttempts = upload.AttemptCount,
                AzureBlobUrl = azureBlobUrl
            };

            _context.UploadHistory.Add(historyRecord);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Completed upload: {FileName} -> {BlobUrl}", upload.FileName, azureBlobUrl);
        }

        public async Task IncrementAttemptAsync(int id, string? errorMessage = null)
        {
            var upload = await _context.UploadQueue.FindAsync(id);
            if (upload == null) return;

            upload.AttemptCount++;
            upload.LastAttemptAt = DateTime.UtcNow;
            upload.ErrorMessage = errorMessage;

            // Mark as failed if max retries reached
            if (upload.AttemptCount >= upload.MaxRetries)
            {
                upload.Status = FileStatus.Failed;
                
                // Archive failed upload to history
                var historyRecord = new UploadHistory
                {
                    FileName = upload.FileName,
                    FileType = upload.FileType,
                    Source = upload.Source,
                    FinalStatus = FileStatus.Failed,
                    CompletedAt = DateTime.UtcNow,
                    FileSizeBytes = upload.FileSizeBytes,
                    TotalAttempts = upload.AttemptCount,
                    FinalErrorMessage = errorMessage
                };
                
                _context.UploadHistory.Add(historyRecord);
            }

            await _context.SaveChangesAsync();
            _logger.LogWarning("Upload attempt {AttemptCount}/{MaxRetries} failed for {FileName}: {Error}", 
                upload.AttemptCount, upload.MaxRetries, upload.FileName, errorMessage);
        }

        public async Task ResetFailedUploadsAsync()
        {
            var failedUploads = await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Failed)
                .ToListAsync();

            foreach (var upload in failedUploads)
            {
                upload.Status = FileStatus.Pending;
                upload.AttemptCount = 0;
                upload.ErrorMessage = null;
                upload.LastAttemptAt = null;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Reset {Count} failed uploads to pending", failedUploads.Count);
        }

        public async Task<bool> IsDuplicateAsync(string hash)
        {
            return await _context.UploadQueue.AnyAsync(u => u.Hash == hash);
        }

        public async Task ArchiveCompletedUploadsAsync(int olderThanDays = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
            var completedUploads = await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Completed && u.CompletedAt < cutoffDate)
                .ToListAsync();

            foreach (var upload in completedUploads)
            {
                upload.Status = FileStatus.Archived;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Archived {Count} completed uploads older than {Days} days", completedUploads.Count, olderThanDays);
        }

        public async Task<IEnumerable<UploadQueue>> GetRecentUploadsAsync(int count = 50)
        {
            return await _context.UploadQueue
                .Include(u => u.UploadProgresses)
                .OrderByDescending(u => u.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<UploadQueue?> GetUploadByIdAsync(int id)
        {
            return await _context.UploadQueue
                .Include(u => u.UploadProgresses)
                .FirstOrDefaultAsync(u => u.Id == id);
        }
    }
}
