using Microsoft.VisualStudio.TestTools.UnitTesting;
using AzureGateway.Api.Utilities;
using System.IO;

namespace AzureGateway.APi.Tests
{
    [TestClass]
    public class FolderPathTests
    {
        private string _testDirectory;

        [TestInitialize]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "AzureGatewayTest", Guid.NewGuid().ToString());
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [TestMethod]
        public void NormalizeFolderPath_WithValidPath_ReturnsNormalizedPath()
        {
            // Arrange
            var testPath = Path.Combine(_testDirectory, "subfolder");

            // Act
            var result = FileHelper.NormalizeFolderPath(testPath, createIfNotExists: true);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(Directory.Exists(result));
            Assert.AreEqual(Path.GetFullPath(testPath), result);
        }

        [TestMethod]
        public void NormalizeFolderPath_WithRelativePath_ReturnsAbsolutePath()
        {
            // Arrange
            var relativePath = "testfolder";

            // Act
            var result = FileHelper.NormalizeFolderPath(relativePath, createIfNotExists: true);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(Path.IsPathRooted(result));
            Assert.IsTrue(Directory.Exists(result));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void NormalizeFolderPath_WithEmptyPath_ThrowsArgumentException()
        {
            // Act
            FileHelper.NormalizeFolderPath("", createIfNotExists: true);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void NormalizeFolderPath_WithNullPath_ThrowsArgumentException()
        {
            // Act
            FileHelper.NormalizeFolderPath(null, createIfNotExists: true);
        }

        [TestMethod]
        [ExpectedException(typeof(DirectoryNotFoundException))]
        public void NormalizeFolderPath_WithNonExistentPathAndCreateFalse_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testDirectory, "nonexistent");

            // Act
            FileHelper.NormalizeFolderPath(nonExistentPath, createIfNotExists: false);
        }

        [TestMethod]
        public void NormalizeFolderPath_WithExistingPath_ReturnsSamePath()
        {
            // Arrange
            Directory.CreateDirectory(_testDirectory);
            var existingPath = _testDirectory;

            // Act
            var result = FileHelper.NormalizeFolderPath(existingPath, createIfNotExists: false);

            // Assert
            Assert.AreEqual(Path.GetFullPath(existingPath), result);
        }
    }
}
