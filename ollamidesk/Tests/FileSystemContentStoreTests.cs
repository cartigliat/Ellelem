using Microsoft.VisualStudio.TestTools.UnitTesting;
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace ollamidesk.Tests.RAG.Services
{
    [TestClass]
    public class FileSystemContentStoreTests
    {
        private StorageSettings _storageSettings = default!; // Initialized to suppress CS8618
        private RagDiagnosticsService _mockDiagnosticsService = default!; // Initialized to suppress CS8618

        [TestInitialize]
        public void Setup()
        {
            _mockDiagnosticsService = new RagDiagnosticsService(new DiagnosticsSettings());

            string baseTestPath = Path.Combine(Path.GetTempPath(), "FileSystemContentStoreTests");
            _storageSettings = new StorageSettings
            {
                BasePath = baseTestPath // Correctly setting BasePath as it has a setter
            };

            if (Directory.Exists(baseTestPath))
            {
                Directory.Delete(baseTestPath, true);
            }
            // Directories are created by the FileSystemContentStore constructor through StorageSettings.BasePath
        }

        [TestCleanup]
        public void Cleanup()
        {
            string baseTestPath = _storageSettings.BasePath;
            if (Directory.Exists(baseTestPath))
            {
                try
                {
                    Directory.Delete(baseTestPath, true);
                }
                catch (Exception) { /* Best effort cleanup */ }
            }
        }

        private FileSystemContentStore CreateStore()
        {
            return new FileSystemContentStore(_storageSettings, _mockDiagnosticsService);
        }

        [TestMethod]
        [Description("Tests that DeleteContentAsync re-throws an exception if File.Delete fails.")]
        public virtual async Task DeleteContentAsync_ThrowsException_WhenFileDeleteFails()
        {
            var store = CreateStore();
            string documentId = "testDoc1";
            string contentFilePath = Path.Combine(_storageSettings.DocumentsFolder, $"{documentId}.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(contentFilePath)!);

            await File.WriteAllTextAsync(contentFilePath, "Some content");

            try
            {
                using (var fs = new FileStream(contentFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    await Assert.ThrowsExceptionAsync<IOException>(async () =>
                    {
                        await store.DeleteContentAsync(documentId);
                    }, "Expected IOException when file is locked and delete is attempted.");
                }
            }
            catch (IOException ex)
            {
                Assert.Inconclusive($"Could not reliably lock the file for testing: {ex.Message}. This test relies on the ability to create a scenario where File.Delete() would throw.");
            }
            finally
            {
                if (File.Exists(contentFilePath)) File.Delete(contentFilePath);
            }
        }

        [TestMethod]
        [Description("Tests that DeleteEmbeddingsAsync re-throws an exception if File.Delete fails.")]
        public async Task DeleteEmbeddingsAsync_ThrowsException_WhenFileDeleteFails()
        {
            var store = CreateStore();
            string documentId = "testDoc2";
            string embeddingsFilePath = Path.Combine(_storageSettings.EmbeddingsFolder, $"{documentId}.json");

            Directory.CreateDirectory(Path.GetDirectoryName(embeddingsFilePath)!);

            await File.WriteAllTextAsync(embeddingsFilePath, "{}");

            try
            {
                using (var fs = new FileStream(embeddingsFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
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
                if (File.Exists(embeddingsFilePath)) File.Delete(embeddingsFilePath);
            }
        }
    }
}