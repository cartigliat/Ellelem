using Microsoft.VisualStudio.TestTools.UnitTesting;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ollamidesk.RAG.Services;
using Moq;

namespace ollamidesk.Tests.RAG.Integration
{
    [TestClass]
    public class DocumentDeletionIntegrationTests
    {
        private string _baseTestPath = default!;
        private StorageSettings _storageSettings = default!;
        private RagDiagnosticsService _diagnosticsService = default!;
        private JsonMetadataStore _metadataStore = default!;
        private FileSystemContentStore _contentStore = default!;
        private SqliteConnectionProvider _sqliteConnectionProvider = default!;
        private SqliteVectorStore _vectorStore = default!;
        private FileSystemDocumentRepository _documentRepository = default!;
        private DocumentProcessorFactory _documentProcessorFactory = default!;
        private DocumentManagementService _documentManagementService = default!;

        public class TestFailureFileSystemContentStore : FileSystemContentStore
        {
            public string FailDeleteContentForDocId { get; set; } = string.Empty;
            public Exception ExceptionToThrowOnDeleteContent { get; set; } = new IOException("Simulated content deletion failure.");

            public TestFailureFileSystemContentStore(StorageSettings settings, RagDiagnosticsService diag)
                : base(settings, diag) { }

            public override async Task DeleteContentAsync(string documentId)
            {
                if (documentId == FailDeleteContentForDocId)
                {
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
                BasePath = _baseTestPath // Correctly setting BasePath
            };

            _diagnosticsService = new RagDiagnosticsService(new DiagnosticsSettings());

            _metadataStore = new JsonMetadataStore(_storageSettings, _diagnosticsService);
            _contentStore = new FileSystemContentStore(_storageSettings, _diagnosticsService);

            _sqliteConnectionProvider = new SqliteConnectionProvider(_storageSettings, _diagnosticsService);
            _sqliteConnectionProvider.InitializeDatabaseAsync().GetAwaiter().GetResult();
            _vectorStore = new SqliteVectorStore(_sqliteConnectionProvider, _diagnosticsService);

            // FIXED: Updated constructor to include IVectorStore parameter
            _documentRepository = new FileSystemDocumentRepository(_metadataStore, _contentStore, _vectorStore, _diagnosticsService);

            // Mock a simple IDocumentProcessor for the factory
            var mockTextProcessor = new Mock<IDocumentProcessor>();
            mockTextProcessor.Setup(p => p.CanProcess(".txt")).Returns(true);
            mockTextProcessor.Setup(p => p.SupportedExtensions).Returns(new[] { ".txt" });
            mockTextProcessor.Setup(p => p.ExtractTextAsync(It.IsAny<string>())).ReturnsAsync("mock text content");
            mockTextProcessor.Setup(p => p.SupportsStructuredExtraction).Returns(true);
            mockTextProcessor.Setup(p => p.ExtractStructuredContentAsync(It.IsAny<string>())).ReturnsAsync(new StructuredDocument());

            var processors = new List<IDocumentProcessor> { mockTextProcessor.Object };
            _documentProcessorFactory = new DocumentProcessorFactory(processors, _diagnosticsService);

            _documentManagementService = new DocumentManagementService(
                _documentRepository,
                _diagnosticsService,
                _documentProcessorFactory);
        }

        [TestCleanup]
        public void Cleanup()
        {
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
                }
            }
        }

        private async Task<Document> AddAndProcessTestDocument(string docName = "test.txt", string content = "This is test content.", bool createEmbeddings = true)
        {
            string sourceTempFilePath = Path.Combine(Path.GetTempPath(), docName);
            await File.WriteAllTextAsync(sourceTempFilePath, content);

            var document = await _documentManagementService.AddDocumentAsync(sourceTempFilePath);
            Assert.IsNotNull(document, "Document should be added successfully.");

            if (File.Exists(sourceTempFilePath))
            {
                File.Delete(sourceTempFilePath);
            }

            if (createEmbeddings)
            {
                document.IsProcessed = true;
                document.Chunks = new List<DocumentChunk>
                {
                    new DocumentChunk { Id = $"{document.Id}_chunk1", DocumentId = document.Id, Content = "Test chunk 1", Embedding = new float[] { 0.1f, 0.2f }, Source = document.Name }
                };

                await _vectorStore.AddVectorsAsync(document.Chunks); // Corrected method call

                await _contentStore.SaveEmbeddingsAsync(document.Id, document.Chunks);

                var metadata = await _metadataStore.GetMetadataByIdAsync(document.Id);
                Assert.IsNotNull(metadata, "Metadata should exist after adding document.");
                metadata!.IsProcessed = true;
                metadata.HasEmbeddings = true;
                await _metadataStore.SaveMetadataAsync(metadata);
            }

            return await _documentManagementService.GetDocumentAsync(document.Id);
        }

        [TestMethod]
        public async Task DeleteDocument_FullProcess_SuccessfullyDeletesAllArtifacts()
        {
            var document = await AddAndProcessTestDocument("full_delete_test.txt", "Content for full deletion.");
            Assert.IsTrue(document.IsProcessed, "Document should be marked as processed.");
            var initialMetadata = await _metadataStore.GetMetadataByIdAsync(document.Id);
            Assert.IsNotNull(initialMetadata, "Metadata should exist initially.");
            Assert.IsTrue(initialMetadata!.HasEmbeddings, "Metadata should indicate embeddings exist.");

            Assert.IsTrue(File.Exists(Path.Combine(_storageSettings.DocumentsFolder, $"{document.Id}.txt")), "Content file should exist initially.");
            Assert.IsTrue(File.Exists(Path.Combine(_storageSettings.EmbeddingsFolder, $"{document.Id}.json")), "Embeddings file should exist initially.");

            // Using SearchInDocumentsAsync to check for existence of chunks in vector store
            var chunksInDb = await _vectorStore.SearchInDocumentsAsync(new float[] { 0.1f, 0.2f }, new List<string> { document.Id });
            Assert.IsTrue(chunksInDb.Any(), "Vector store should have chunks initially.");

            Assert.IsNotNull(await _metadataStore.GetMetadataByIdAsync(document.Id), "Metadata should exist initially.");

            await _documentManagementService.DeleteDocumentAsync(document.Id);

            var metadataAfterDelete = await _metadataStore.GetMetadataByIdAsync(document.Id);
            Assert.IsNull(metadataAfterDelete, "Metadata should be deleted.");

            Assert.IsFalse(File.Exists(Path.Combine(_storageSettings.DocumentsFolder, $"{document.Id}.txt")), "Content file should be deleted.");
            Assert.IsFalse(File.Exists(Path.Combine(_storageSettings.EmbeddingsFolder, $"{document.Id}.json")), "Embeddings file should be deleted.");

            try
            {
                chunksInDb = await _vectorStore.SearchInDocumentsAsync(new float[] { 0.1f, 0.2f }, new List<string> { document.Id });
                Assert.IsFalse(chunksInDb.Any(), "Vector store should not have chunks after delete.");
            }
            catch (KeyNotFoundException)
            {
                // Acceptable if SearchInDocumentsAsync throws for non-existent doc ID
            }
            catch (Exception ex)
            {
                Assert.Fail($"Unexpected exception from SearchInDocumentsAsync: {ex.Message}");
            }
        }

        [TestMethod]
        public async Task DeleteDocument_NonExistentDocument_HandlesGracefully()
        {
            string nonExistentId = Guid.NewGuid().ToString();

            try
            {
                await _documentManagementService.DeleteDocumentAsync(nonExistentId);
            }
            catch (Exception ex)
            {
                Assert.Fail($"DeleteDocumentAsync should not throw for a non-existent ID, but threw: {ex.GetType().Name} - {ex.Message}");
            }

            _diagnosticsService.Log(DiagnosticLevel.Info, "TestCheck", $"Checked deletion of non-existent ID {nonExistentId}. Expecting no errors in preceding logs from service for this ID.");
        }

        [TestMethod]
        public async Task DeleteDocument_ContentFileDeletionFails_MetadataAndEmbeddingsAreNotDeleted()
        {
            var failingContentStore = new TestFailureFileSystemContentStore(_storageSettings, _diagnosticsService);
            // FIXED: Updated constructor to include IVectorStore parameter
            var repositoryWithFailingContentStore = new FileSystemDocumentRepository(_metadataStore, failingContentStore, _vectorStore, _diagnosticsService);
            var serviceWithFailingRepo = new DocumentManagementService(repositoryWithFailingContentStore, _diagnosticsService, _documentProcessorFactory);

            var document = await AddAndProcessTestDocument("fail_content_delete.txt", "Content for failed deletion test.");
            failingContentStore.FailDeleteContentForDocId = document.Id;

            string contentFilePath = Path.Combine(_storageSettings.DocumentsFolder, $"{document.Id}.txt");
            string embeddingsFilePath = Path.Combine(_storageSettings.EmbeddingsFolder, $"{document.Id}.json");
            Assert.IsTrue(File.Exists(contentFilePath), "Content file should exist initially.");
            Assert.IsTrue(File.Exists(embeddingsFilePath), "Embeddings file should exist initially.");

            var chunksInDb = await _vectorStore.SearchInDocumentsAsync(new float[] { 0.1f, 0.2f }, new List<string> { document.Id });
            Assert.IsTrue(chunksInDb.Any(), "Vector store should have chunks initially.");

            Assert.IsNotNull(await _metadataStore.GetMetadataByIdAsync(document.Id), "Metadata should exist initially.");

            await Assert.ThrowsExceptionAsync<IOException>(async () =>
            {
                await serviceWithFailingRepo.DeleteDocumentAsync(document.Id);
            }, "Expected an IOException (or wrapped) when content deletion fails.");

            Assert.IsNotNull(await _metadataStore.GetMetadataByIdAsync(document.Id), "Metadata should still exist after content deletion failure.");
            Assert.IsTrue(File.Exists(embeddingsFilePath), "Embeddings file should still exist after content deletion failure.");

            chunksInDb = await _vectorStore.SearchInDocumentsAsync(new float[] { 0.1f, 0.2f }, new List<string> { document.Id });
            Assert.IsTrue(chunksInDb.Any(), "Vector store chunks should still exist after content deletion failure.");

            Assert.IsTrue(File.Exists(contentFilePath), "Content file itself should still exist as its deletion failed.");
        }
    }
}