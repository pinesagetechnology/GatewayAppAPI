using System.Security.Cryptography;
using System.Text;

namespace AzureGateway.Api.Utilities
{
    public static class FileHelper
    {
        public static async Task<string> CalculateFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
            return Convert.ToHexString(hashBytes);
        }

        public static FileType GetFileType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".json" => FileType.Json,
                ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" => FileType.Image,
                _ => FileType.Other
            };
        }

        public static async Task<bool> IsValidJsonFileAsync(string filePath)
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                System.Text.Json.JsonDocument.Parse(content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsValidImageFile(string filePath)
        {
            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return validExtensions.Contains(extension);
        }

        public static string GetSafeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = new StringBuilder();
            
            foreach (var c in fileName)
            {
                if (!invalidChars.Contains(c))
                    safeName.Append(c);
                else
                    safeName.Append('_');
            }
            
            return safeName.ToString();
        }

        public static async Task MoveFileToArchiveAsync(string sourceFile, string archiveDirectory)
        {
            try
            {
                if (!Directory.Exists(archiveDirectory))
                {
                    Directory.CreateDirectory(archiveDirectory);
                }

                var fileName = Path.GetFileName(sourceFile);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var archivedFileName = $"{timestamp}_{fileName}";
                var destinationPath = Path.Combine(archiveDirectory, archivedFileName);

                // Use File.Move for cross-platform compatibility
                File.Move(sourceFile, destinationPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to archive file {sourceFile}: {ex.Message}", ex);
            }
        }
    }
}
