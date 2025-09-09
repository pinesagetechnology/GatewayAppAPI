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

        /// <summary>
        /// Normalizes and validates a folder path for cross-platform compatibility
        /// </summary>
        /// <param name="folderPath">The folder path to normalize</param>
        /// <param name="createIfNotExists">Whether to create the folder if it doesn't exist</param>
        /// <returns>The normalized folder path</returns>
        /// <exception cref="ArgumentException">Thrown when the path is invalid</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the folder doesn't exist and cannot be created</exception>
        public static string NormalizeFolderPath(string folderPath, bool createIfNotExists = true)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("Folder path cannot be null or empty", nameof(folderPath));
            }

            // Normalize the path for cross-platform compatibility
            var normalizedPath = Path.GetFullPath(folderPath);

            // Check if directory exists
            if (!Directory.Exists(normalizedPath))
            {
                if (createIfNotExists)
                {
                    try
                    {
                        Directory.CreateDirectory(normalizedPath);
                    }
                    catch (Exception ex)
                    {
                        throw new DirectoryNotFoundException($"Failed to create folder path: {normalizedPath}. Error: {ex.Message}", ex);
                    }
                }
                else
                {
                    throw new DirectoryNotFoundException($"Folder path does not exist: {normalizedPath}");
                }
            }

            return normalizedPath;
        }
    }
}
