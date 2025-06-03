// ollamidesk/Tests/DocumentDeletionIntegrationTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations; // For DocumentProcessorFactory
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

// If Moq were available and usable for concrete types with passthrough:
// using Moq;

namespace ollamidesk.Tests.RAG.Integration
{
    [TestClass]
    public class DocumentDeletionIntegrationTests
    {
        private string _baseTestPath;
        private StorageSettings _storageSettings;
        private RagDiagnosticsService _diagnosticsService;
        private JsonMetadataStore _metadataStore;
        private FileSystemContentStore _contentStore;
        private SqliteConnectionProvider _sqliteConnectionProvider;
        private SqliteVectorStore _vectorStore;
        private FileSystemDocumentRepository _documentRepository;
        private DocumentProcessorFactory _documentProcessorFactory;
        private DocumentManagementService _documentManagementService;

        // Test-specific wrapper/derived class for FileSystemContentStore to simulate failure
        public class TestFailureFileSystemContentStore : FileSystemContentStore
        {
            public string FailDeleteContentForDocId { get; set; }
            public Exception ExceptionToThrowOnDeleteContent { get; set; } = new IOException("Simulated content deletion failure.");

            public TestFailureFileSystemContentStore(StorageSettings settings, RagDiagnosticsService diag)
                : base(settings, diag) { }

            public override async Task DeleteContentAsync(string documentId)
            {
                if (documentId == FailDeleteContentForDocId)
                {
                    // Log the simulated failure for clarity if diagnostics were being checked
                    // base._diagnostics.Log(DiagnosticLevel.Error, "TestFailureFileSystemContentStore", $"Simulating DeleteContentAsync failure for {documentId}");
                    throw ExceptionToThrowOnDeleteContent;
                }
                await base.DeleteContentAsync(documentId);
            }
        }


        [TestInitialize]
        public void Setup()
        {
            _baseTestPath = Path.Combine(Path.GetTempPath(), $"Ollamidesk_IntegrationTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_baseTestPath);

            _storageSettings = new StorageSettings
            {
                BaseStoragePath = _baseTestPath, // Assuming StorageSettings might use this
                DocumentsFolder = Path.Combine(_baseTestPath, "Documents"),
                EmbeddingsFolder = Path.Combine(_baseTestPath, "Embeddings"),
                MetadataFile = Path.Combine(_baseTestPath, "library.json"),
                DatabasePath = Path.Combine(_baseTestPath, "vectors.db")
                // Ensure all paths used by stores are covered by _baseTestPath for cleanup
            };

            Directory.CreateDirectory(_storageSettings.DocumentsFolder);
            Directory.CreateDirectory(_storageSettings.EmbeddingsFolder);
            // Ensure parent directory for MetadataFile and DatabasePath exists if they are not the _baseTestPath itself
            Directory.CreateDirectory(Path.GetDirectoryName(_storageSettings.MetadataFile));
            Directory.CreateDirectory(Path.GetDirectoryName(_storageSettings.DatabasePath));


            // Using a real diagnostics service, but could be a simple mock/fake if needed
            _diagnosticsService = new RagDiagnosticsService(null, null); // Assuming nulls are fine for its dependencies

            _metadataStore = new JsonMetadataStore(_storageSettings, _diagnosticsService);
            _contentStore = new FileSystemContentStore(_storageSettings, _diagnosticsService); // Regular one for most tests

            _sqliteConnectionProvider = new SqliteConnectionProvider(_storageSettings);
            _vectorStore = new SqliteVectorStore(_sqliteConnectionProvider, _diagnosticsService);
            _vectorStore.InitializeAsync().GetAwaiter().GetResult(); // Ensure DB schema is created

            _documentRepository = new FileSystemDocumentRepository(_metadataStore, _contentStore, _diagnosticsService);

            var serviceProviderMock = new Moq.Mock<IServiceProvider>(); // Needed for DocProcessorFactory
            _documentProcessorFactory = new DocumentProcessorFactory(serviceProviderMock.Object);


            _documentManagementService = new DocumentManagementService(
                _documentRepository,
                _diagnosticsService,
                _documentProcessorFactory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Dispose disposable resources if any were kept (e.g. DB connections if not managed by provider)
            (_vectorStore as IDisposable)?.Dispose();
            (_sqliteConnectionProvider as IDisposable)?.Dispose();


            if (Directory.Exists(_baseTestPath))
            {
                try
                {
                    Directory.Delete(_baseTestPath, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not fully clean up test directory {_baseTestPath}. Error: {ex.Message}");
                    // This can happen if files are locked, e.g. SQLite connections not fully released.
                }
            }
        }

        private async Task<Document> AddAndProcessTestDocument(string docName = "test.txt", string content = "This is test content.", bool createEmbeddings = true)
        {
            string tempFilePath = Path.Combine(_storageSettings.DocumentsFolder, docName); // Place in docs folder for "Add"
            await File.WriteAllTextAsync(tempFilePath, content);

            // Add document using the service - this creates initial metadata and content file (handled by service)
            var document = await _documentManagementService.AddDocumentAsync(tempFilePath);
            Assert.IsNotNull(document, "Document should be added successfully.");
            // AddDocumentAsync in DMS now also saves content via content store.

            if (createEmbeddings)
            {
                document.IsProcessed = true;
                document.Chunks = new List<DocumentChunk>
                {
                    new DocumentChunk { Id = $"{document.Id}_chunk1", DocumentId = document.Id, Text = "Test chunk 1", Embedding = new float[] { 0.1f, 0.2f } }
                };
                await _vectorStore.AddVectorsAsync(document.Id, document.Chunks); // Use overload with documentId

                // Manually save embeddings file via content store as DMS.Save (called by Add) might not do embeddings
                await _contentStore.SaveEmbeddingsAsync(document.Id, document.Chunks);

                var metadata = await _metadataStore.GetMetadataByIdAsync(document.Id);
                metadata.IsProcessed = true;
                metadata.HasEmbeddings = true; // Crucial for repository logic
                await _metadataStore.SaveMetadataAsync(metadata); // Save updated metadata
            }

            // Delete the temporary source file if it's different from where content store saves it
            // (AddDocumentAsync copies it, content store creates its own version)
            // The actual content file is now managed by FileSystemContentStore.
            if (File.Exists(tempFilePath) && Path.GetDirectoryName(tempFilePath) == _storageSettings.DocumentsFolder)
            {
                 // If the tempFilePath was directly in the target DocumentsFolder (which it is here),
                 // it might be the same file AddDocumentAsync creates via FileSystemContentStore.
                 // Let's ensure our original tempFilePath is gone if it was just a source.
                 // The FileSystemContentStore uses <docId>.txt, AddDocumentAsync might have used original name before passing content.
                 // This part is a bit tricky depending on exact AddDocumentAsync logic.
                 // For this test, we assume AddDocumentAsync has handled the file passed to it, and subsequent operations
                 // use the ID-based paths from FileSystemContentStore.
            }


            return await _documentManagementService.GetDocumentAsync(document.Id); // Reload to get latest state
        }

        [TestMethod]
        public async Task DeleteDocument_FullProcess_SuccessfullyDeletesAllArtifacts()
        {
            // Arrange
            var document = await AddAndProcessTestDocument("full_delete_test.txt", "Content for full deletion.");
            Assert.IsTrue(document.IsProcessed, "Document should be marked as processed.");
            Assert.IsTrue((await _metadataStore.GetMetadataByIdAsync(document.Id)).HasEmbeddings, "Metadata should indicate embeddings exist.");

            // Verify initial state
            Assert.IsTrue(File.Exists(Path.Combine(_storageSettings.DocumentsFolder, $"{document.Id}.txt")), "Content file should exist initially.");
            Assert.IsTrue(File.Exists(Path.Combine(_storageSettings.EmbeddingsFolder, $"{document.Id}.json")), "Embeddings file should exist initially.");
            var chunksInDb = await _vectorStore.GetDocumentChunksAsync(document.Id);
            Assert.IsTrue(chunksInDb.Any(), "Vector store should have chunks initially.");
            Assert.IsNotNull(await _metadataStore.GetMetadataByIdAsync(document.Id), "Metadata should exist initially.");

            // Act
            await _documentManagementService.DeleteDocumentAsync(document.Id);

            // Assert
            // 1. Metadata
            var metadataAfterDelete = await _metadataStore.GetMetadataByIdAsync(document.Id);
            Assert.IsNull(metadataAfterDelete, "Metadata should be deleted.");
            // Or, if library.json is kept and entry removed:
            // var allMetadata = await _metadataStore.LoadMetadataAsync();
            // Assert.IsFalse(allMetadata.ContainsKey(document.Id), "Document ID should not be in metadata list after delete.");


            // 2. Content file
            Assert.IsFalse(File.Exists(Path.Combine(_storageSettings.DocumentsFolder, $"{document.Id}.txt")), "Content file should be deleted.");

            // 3. Embeddings file
            Assert.IsFalse(File.Exists(Path.Combine(_storageSettings.EmbeddingsFolder, $"{document.Id}.json")), "Embeddings file should be deleted.");

            // 4. Vector Store entries
            try
            {
                var chunksAfterDelete = await _vectorStore.GetDocumentChunksAsync(document.Id);
                 Assert.IsFalse(chunksAfterDelete.Any(), "Vector store should not have chunks after delete.");
            }
            catch(KeyNotFoundException)
            {
                // This is also an acceptable outcome if GetDocumentChunksAsync throws when docId is unknown
            }
            catch(Exception ex)
            {
                // If it throws something else, that's a problem unless specified.
                // For SqliteVectorStore, it seems to return empty list rather than throw for unknown docId.
                Assert.Fail($"Unexpected exception from GetDocumentChunksAsync: {ex.Message}");
            }
            // A more direct check might be:
            // Assert.AreEqual(0, await _vectorStore.GetVectorCountForDocumentAsync(document.Id)); // Assuming such a method exists or can be added
        }

        [TestMethod]
        public async Task DeleteDocument_NonExistentDocument_HandlesGracefully()
        {
            // Arrange
            string nonExistentId = Guid.NewGuid().ToString();

            // Act & Assert
            try
            {
                await _documentManagementService.DeleteDocumentAsync(nonExistentId);
                // No exception is expected, or a specific one if designed that way.
                // FileSystemDocumentRepository and its underlying stores are generally designed
                // to not throw if a file/entry to be deleted doesn't exist.
            }
            catch (Exception ex)
            {
                // If KeyNotFoundException is thrown by GetDocumentByIdAsync (which Delete might call first),
                // that could be acceptable, but the stores themselves usually don't throw on delete-if-missing.
                // The current FileSystemDocumentRepository.DeleteDocumentAsync doesn't fetch first, it directly calls delete on stores.
                Assert.Fail($"DeleteDocumentAsync should not throw for a non-existent ID, but threw: {ex.GetType().Name} - {ex.Message}");
            }

            // Verify no files were accidentally created/deleted (hard to check for non-existence directly)
            // Primarily, this test ensures no unexpected exceptions.
            // We can also check logs if verbose logging is enabled for "not found" type messages.
            _diagnosticsService.Log(DiagnosticLevel.Info, "TestCheck", $"Checked deletion of non-existent ID {nonExistentId}. Expecting no errors in preceding logs from service for this ID.");
        }

        [TestMethod]
        public async Task DeleteDocument_ContentFileDeletionFails_MetadataAndEmbeddingsAreNotDeleted()
        {
            // Arrange: Use the TestFailureFileSystemContentStore for this test
            // Re-initialize services that depend on _contentStore
            var failingContentStore = new TestFailureFileSystemContentStore(_storageSettings, _diagnosticsService);
            var repositoryWithFailingContentStore = new FileSystemDocumentRepository(_metadataStore, failingContentStore, _diagnosticsService);
            var serviceWithFailingRepo = new DocumentManagementService(repositoryWithFailingContentStore, _diagnosticsService, _documentProcessorFactory);

            var document = await AddAndProcessTestDocument("fail_content_delete.txt", "Content for failed deletion test.");
            failingContentStore.FailDeleteContentForDocId = document.Id; // Configure failure for this specific document

            // Verify initial state
            string contentFilePath = Path.Combine(_storageSettings.DocumentsFolder, $"{document.Id}.txt");
            string embeddingsFilePath = Path.Combine(_storageSettings.EmbeddingsFolder, $"{document.Id}.json");
            Assert.IsTrue(File.Exists(contentFilePath), "Content file should exist initially.");
            Assert.IsTrue(File.Exists(embeddingsFilePath), "Embeddings file should exist initially.");
            Assert.IsTrue((await _vectorStore.GetDocumentChunksAsync(document.Id)).Any(), "Vector store should have chunks initially.");
            Assert.IsNotNull(await _metadataStore.GetMetadataByIdAsync(document.Id), "Metadata should exist initially.");

            // Act & Assert for Exception
            await Assert.ThrowsExceptionAsync<IOException>(async () =>
            {
                await serviceWithFailingRepo.DeleteDocumentAsync(document.Id);
            }, "Expected an IOException (or wrapped) when content deletion fails.");

            // Assert that other artifacts still exist
            Assert.IsNotNull(await _metadataStore.GetMetadataByIdAsync(document.Id), "Metadata should still exist after content deletion failure.");
            Assert.IsTrue(File.Exists(embeddingsFilePath), "Embeddings file should still exist after content deletion failure.");
            Assert.IsTrue((await _vectorStore.GetDocumentChunksAsync(document.Id)).Any(), "Vector store chunks should still exist after content deletion failure.");
            Assert.IsTrue(File.Exists(contentFilePath), "Content file itself should still exist as its deletion failed.");
        }
    }
}
