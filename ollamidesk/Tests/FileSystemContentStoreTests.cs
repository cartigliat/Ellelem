// ollamidesk/Tests/FileSystemContentStoreTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent; // Required for ConcurrentDictionary if used directly in mocks

// Moq would be referenced via: using Moq;

namespace ollamidesk.Tests.RAG.Services
{
    [TestClass]
    public class FileSystemContentStoreTests
    {
        private StorageSettings _storageSettings;
        private RagDiagnosticsService _mockDiagnosticsService;
        // In a real scenario with Moq: private Mock<RagDiagnosticsService> _mockDiagnosticsServiceMoq;

        [TestInitialize]
        public void Setup()
        {
            // Setup mock for RagDiagnosticsService
            // _mockDiagnosticsServiceMoq = new Mock<RagDiagnosticsService>(null, null); // Assuming constructor takes ICommandLineInterface, ILogSink
            // _mockDiagnosticsService = _mockDiagnosticsServiceMoq.Object;
            _mockDiagnosticsService = new RagDiagnosticsService(null, null); // Simplified for no Moq

            // Setup temporary storage paths for tests
            string baseTestPath = Path.Combine(Path.GetTempPath(), "FileSystemContentStoreTests");
            _storageSettings = new StorageSettings
            {
                DocumentsFolder = Path.Combine(baseTestPath, "Documents"),
                EmbeddingsFolder = Path.Combine(baseTestPath, "Embeddings")
            };

            // Ensure clean state for each test
            if (Directory.Exists(baseTestPath))
            {
                Directory.Delete(baseTestPath, true);
            }
            Directory.CreateDirectory(_storageSettings.DocumentsFolder);
            Directory.CreateDirectory(_storageSettings.EmbeddingsFolder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            string baseTestPath = Path.Combine(Path.GetTempPath(), "FileSystemContentStoreTests");
            if (Directory.Exists(baseTestPath))
            {
                try
                {
                    Directory.Delete(baseTestPath, true);
                }
                catch (Exception) { /* Best effort cleanup */ }
            }
        }

        // Helper method to create a FileSystemContentStore instance
        private FileSystemContentStore CreateStore()
        {
            return new FileSystemContentStore(_storageSettings, _mockDiagnosticsService);
        }

        [TestMethod]
        [Description("Tests that DeleteContentAsync re-throws an exception if File.Delete fails.")]
        public async Task DeleteContentAsync_ThrowsException_WhenFileDeleteFails()
        {
            var store = CreateStore();
            string documentId = "testDoc1";
            string contentFilePath = Path.Combine(_storageSettings.DocumentsFolder, $"{documentId}.txt");

            // Create a file to be deleted
            await File.WriteAllTextAsync(contentFilePath, "Some content");

            // **LIMITATION**: We cannot directly mock File.Delete to throw an IOException on demand
            // without a file system abstraction or advanced mocking tools (like Shims).
            // This test relies on the fact that if ANY exception occurs during File.Delete()
            // (e.g., file locked, permissions issue - which are hard to simulate here),
            // the 'throw;' statement added previously in FileSystemContentStore will propagate it.

            // To simulate a condition where File.Delete *might* fail, we could try to lock the file,
            // but this is unreliable across platforms and test environments.
            // For this conceptual test, we'll assume an Exception occurs and expect it to be re-thrown.
            // The actual trigger for the exception during File.Delete is outside our control here.

            // If we could mock, it would be:
            // MockFileSystem.Setup(fs => fs.Delete(contentFilePath)).Throws<IOException>();

            // As a placeholder for demonstrating the test structure, we'll try to delete a file
            // that we then immediately try to delete again after it's gone, though File.Delete
            // itself doesn't throw if the file is not found.
            // A more realistic approach would be to have an IFileSystem interface.

            // This test will pass if *any* exception is thrown from DeleteContentAsync.
            // The critical part is that FileSystemContentStore now has `throw;` in its catch block.

            // To make this test *actually fail* in a controlled way to prove the re-throw,
            // we'd need to modify FileSystemContentStore to use a mockable file service.
            // For now, we are testing the re-throw behavior assuming an underlying exception.

            // Let's try to provoke an error by locking the file (this is platform-dependent and might not work)
            try
            {
                using (var fs = new FileStream(contentFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    // File is now locked
                    await Assert.ThrowsExceptionAsync<IOException>(async () =>
                    {
                        await store.DeleteContentAsync(documentId);
                    }, "Expected IOException when file is locked and delete is attempted.");
                }
            }
            catch (IOException ex)
            {
                 // This catch block is for the FileStream lock itself, if it fails.
                 // If the file cannot be locked, the Assert.ThrowsExceptionAsync above might not run as intended.
                 Assert.Inconclusive($"Could not reliably lock the file for testing: {ex.Message}. This test relies on the ability to create a scenario where File.Delete() would throw.");
            }
            finally
            {
                // Ensure the file is deleted if it wasn't by the test or if the lock failed.
                if(File.Exists(contentFilePath)) File.Delete(contentFilePath);
            }
        }

        [TestMethod]
        [Description("Tests that DeleteEmbeddingsAsync re-throws an exception if File.Delete fails.")]
        public async Task DeleteEmbeddingsAsync_ThrowsException_WhenFileDeleteFails()
        {
            var store = CreateStore();
            string documentId = "testDoc2";
            string embeddingsFilePath = Path.Combine(_storageSettings.EmbeddingsFolder, $"{documentId}.json");

            await File.WriteAllTextAsync(embeddingsFilePath, "{}"); // Create a dummy embeddings file

            // Similar limitation as above for File.Delete.
            // We rely on the `throw;` in FileSystemContentStore.

            try
            {
                using (var fs = new FileStream(embeddingsFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    // File is now locked
                    await Assert.ThrowsExceptionAsync<IOException>(async () =>
                    {
                        await store.DeleteEmbeddingsAsync(documentId);
                    }, "Expected IOException when file is locked and delete is attempted.");
                }
            }
            catch (IOException ex)
            {
                 Assert.Inconclusive($"Could not reliably lock the file for testing: {ex.Message}. This test relies on the ability to create a scenario where File.Delete() would throw.");
            }
            finally
            {
                if(File.Exists(embeddingsFilePath)) File.Delete(embeddingsFilePath);
            }
        }
    }
}
