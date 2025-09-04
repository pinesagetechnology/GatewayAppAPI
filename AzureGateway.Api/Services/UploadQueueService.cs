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
            _logger.LogDebug("UploadQueueService initialized");
        }

        public async Task<UploadQueue> AddToQueueAsync(string filePath, FileType fileType, DataSource source, long fileSize, string? hash = null)
        {
            _logger.LogInformation("Adding file to upload queue: {FilePath} ({FileSize} bytes, Type: {FileType}, Source: {Source})", 
                filePath, fileSize, fileType, source);

            // Check for duplicates if hash is provided
            if (!string.IsNullOrEmpty(hash))
            {
                _logger.LogDebug("Checking for duplicate file with hash: {Hash}", hash);
                if (await IsDuplicateAsync(hash))
                {
                    _logger.LogInformation("Duplicate file detected with hash: {Hash}", hash);
                    var existing = await _context.UploadQueue.FirstAsync(u => u.Hash == hash);
                    _logger.LogInformation("Returning existing upload item: ID {Id}", existing.Id);
                    return existing;
                }
                else
                {
                    _logger.LogDebug("No duplicate found for hash: {Hash}", hash);
                }
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

            _logger.LogDebug("Creating new upload queue item for file: {FileName}", fileName);
            _context.UploadQueue.Add(uploadItem);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully added file to upload queue: {FileName} (ID: {Id})", fileName, uploadItem.Id);
            return uploadItem;
        }

        public async Task<IEnumerable<UploadQueue>> GetPendingUploadsAsync()
        {
            _logger.LogDebug("Getting pending uploads from queue...");
            var pendingUploads = await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Pending)
                .OrderBy(u => u.CreatedAt)
                .ToListAsync();

            _logger.LogDebug("Found {Count} pending uploads", pendingUploads.Count);
            return pendingUploads;
        }

        public async Task<IEnumerable<UploadQueue>> GetFailedUploadsAsync()
        {
            _logger.LogDebug("Getting failed uploads from queue...");
            var failedUploads = await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Failed)
                .OrderByDescending(u => u.LastAttemptAt)
                .ToListAsync();

            _logger.LogDebug("Found {Count} failed uploads", failedUploads.Count);
            return failedUploads;
        }

        public async Task<UploadQueue?> GetNextUploadAsync()
        {
            _logger.LogDebug("Getting next upload from queue...");
            var nextUpload = await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Pending)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefaultAsync();

            if (nextUpload != null)
            {
                _logger.LogDebug("Next upload: {FileName} (ID: {Id})", nextUpload.FileName, nextUpload.Id);
            }
            else
            {
                _logger.LogDebug("No pending uploads found in queue");
            }

            return nextUpload;
        }

        public async Task UpdateStatusAsync(int id, FileStatus status, string? errorMessage = null)
        {
            _logger.LogInformation("Updating upload {Id} status to {Status}", id, status);
            
            var upload = await _context.UploadQueue.FindAsync(id);
            if (upload == null)
            {
                _logger.LogWarning("Upload {Id} not found, cannot update status", id);
                return;
            }

            var previousStatus = upload.Status;
            upload.Status = status;
            upload.ErrorMessage = errorMessage;
            
            if (status == FileStatus.Completed)
            {
                upload.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation("Upload {Id} marked as completed at {CompletedAt}", id, upload.CompletedAt);
            }

            await _context.SaveChangesAsync();
            _logger.LogDebug("Updated upload {Id} status from {PreviousStatus} to {NewStatus}", 
                id, previousStatus, status);
        }

        public async Task UpdateProgressAsync(int id, long bytesUploaded, long totalBytes, string? statusMessage = null)
        {
            _logger.LogDebug("Updating upload {Id} progress: {Uploaded}/{Total} bytes", id, bytesUploaded, totalBytes);
            
            // Update or create progress record
            var existingProgress = await _context.UploadProgress
                .FirstOrDefaultAsync(p => p.UploadQueueId == id);

            if (existingProgress != null)
            {
                existingProgress.BytesUploaded = bytesUploaded;
                existingProgress.TotalBytes = totalBytes;
                existingProgress.StatusMessage = statusMessage;
                existingProgress.UpdatedAt = DateTime.UtcNow;
                _logger.LogDebug("Updated existing progress record for upload {Id}", id);
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
                _logger.LogDebug("Created new progress record for upload {Id}", id);
            }

            await _context.SaveChangesAsync();
            
            var percentage = totalBytes > 0 ? (bytesUploaded * 100.0 / totalBytes) : 0;
            _logger.LogDebug("Upload {Id} progress: {Percentage:F1}% ({Uploaded}/{Total} bytes)", 
                id, percentage, bytesUploaded, totalBytes);
        }

        public async Task MarkAsCompletedAsync(int id, string azureBlobUrl, long uploadDurationMs)
        {
            _logger.LogInformation("Marking upload {Id} as completed with blob URL: {BlobUrl}", id, azureBlobUrl);
            
            var upload = await _context.UploadQueue.FindAsync(id);
            if (upload == null)
            {
                _logger.LogWarning("Upload {Id} not found, cannot mark as completed", id);
                return;
            }

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

            _logger.LogInformation("Successfully completed upload: {FileName} -> {BlobUrl} in {Duration}ms", 
                upload.FileName, azureBlobUrl, uploadDurationMs);
        }

        public async Task IncrementAttemptAsync(int id, string? errorMessage = null)
        {
            _logger.LogWarning("Incrementing attempt count for upload {Id}. Error: {Error}", id, errorMessage ?? "None");
            
            var upload = await _context.UploadQueue.FindAsync(id);
            if (upload == null)
            {
                _logger.LogWarning("Upload {Id} not found, cannot increment attempt", id);
                return;
            }

            upload.AttemptCount++;
            upload.LastAttemptAt = DateTime.UtcNow;
            upload.ErrorMessage = errorMessage;

            // Mark as failed if max retries reached
            if (upload.AttemptCount >= upload.MaxRetries)
            {
                upload.Status = FileStatus.Failed;
                _logger.LogWarning("Upload {Id} reached max retries ({MaxRetries}), marking as failed", 
                    id, upload.MaxRetries);
                
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
                _logger.LogInformation("Archived failed upload {Id} to history", id);
            }

            await _context.SaveChangesAsync();
            _logger.LogWarning("Upload attempt {AttemptCount}/{MaxRetries} failed for {FileName}: {Error}", 
                upload.AttemptCount, upload.MaxRetries, upload.FileName, errorMessage);
        }

        public async Task ResetFailedUploadsAsync()
        {
            _logger.LogInformation("Resetting failed uploads to pending status...");
            
            var failedUploads = await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Failed)
                .ToListAsync();

            if (failedUploads.Count == 0)
            {
                _logger.LogInformation("No failed uploads found to reset");
                return;
            }

            foreach (var upload in failedUploads)
            {
                upload.Status = FileStatus.Pending;
                upload.AttemptCount = 0;
                upload.ErrorMessage = null;
                upload.LastAttemptAt = null;
                _logger.LogDebug("Reset upload {Id} ({FileName}) to pending", upload.Id, upload.FileName);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully reset {Count} failed uploads to pending status", failedUploads.Count);
        }

        public async Task<bool> IsDuplicateAsync(string hash)
        {
            _logger.LogDebug("Checking for duplicate file with hash: {Hash}", hash);
            var isDuplicate = await _context.UploadQueue.AnyAsync(u => u.Hash == hash);
            _logger.LogDebug("Duplicate check result for hash {Hash}: {IsDuplicate}", hash, isDuplicate);
            return isDuplicate;
        }

        public async Task ArchiveCompletedUploadsAsync(int olderThanDays = 30)
        {
            _logger.LogInformation("Archiving completed uploads older than {Days} days...", olderThanDays);
            
            var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
            var completedUploads = await _context.UploadQueue
                .Where(u => u.Status == FileStatus.Completed && u.CompletedAt < cutoffDate)
                .ToListAsync();

            if (completedUploads.Count == 0)
            {
                _logger.LogInformation("No completed uploads found older than {Days} days", olderThanDays);
                return;
            }

            foreach (var upload in completedUploads)
            {
                upload.Status = FileStatus.Archived;
                _logger.LogDebug("Archived upload {Id} ({FileName}) completed at {CompletedAt}", 
                    upload.Id, upload.FileName, upload.CompletedAt);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully archived {Count} completed uploads older than {Days} days", 
                completedUploads.Count, olderThanDays);
        }

        public async Task<IEnumerable<UploadQueue>> GetRecentUploadsAsync(int count = 50)
        {
            _logger.LogDebug("Getting {Count} recent uploads from queue...", count);
            
            var recentUploads = await _context.UploadQueue
                .Include(u => u.UploadProgresses)
                .OrderByDescending(u => u.CreatedAt)
                .Take(count)
                .ToListAsync();

            _logger.LogDebug("Retrieved {Count} recent uploads", recentUploads.Count);
            return recentUploads;
        }

        public async Task<UploadQueue?> GetUploadByIdAsync(int id)
        {
            _logger.LogDebug("Getting upload by ID: {Id}", id);
            
            var upload = await _context.UploadQueue
                .Include(u => u.UploadProgresses)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (upload != null)
            {
                _logger.LogDebug("Found upload {Id}: {FileName} (Status: {Status})", 
                    id, upload.FileName, upload.Status);
            }
            else
            {
                _logger.LogDebug("Upload {Id} not found", id);
            }

            return upload;
        }

        public async Task<object> GetQueueSummaryAsync()
        {
            _logger.LogDebug("Getting upload queue summary...");
            
            var summary = await _context.UploadQueue
                .GroupBy(u => u.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var totalFiles = await _context.UploadQueue.CountAsync();
            var totalSize = await _context.UploadQueue.SumAsync(u => u.FileSizeBytes);

            var result = new
            {
                TotalFiles = totalFiles,
                TotalSizeBytes = totalSize,
                StatusBreakdown = summary,
                LastUpdated = DateTime.UtcNow
            };

            _logger.LogDebug("Queue summary: {TotalFiles} files, {TotalSize} bytes, {StatusCount} statuses", 
                totalFiles, totalSize, summary.Count);
            
            return result;
        }
    }
}
