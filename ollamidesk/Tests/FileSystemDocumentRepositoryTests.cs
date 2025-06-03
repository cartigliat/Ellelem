// ollamidesk/Tests/FileSystemDocumentRepositoryTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq; // Assuming Moq is available for mocking
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Exceptions; // For custom exceptions if needed
using ollamidesk.Configuration;
using System;
using System.IO; // For IOException
using System.Threading.Tasks;

namespace ollamidesk.Tests.RAG.Services
{
    [TestClass]
    public class FileSystemDocumentRepositoryTests
    {
        private Mock<IMetadataStore> _mockMetadataStore = default!; // Initialize to suppress CS8618
        private Mock<IContentStore> _mockContentStore = default!; // Initialize to suppress CS8618
        private Mock<IVectorStore> _mockVectorStore = default!; // Added missing mock
        private Mock<RagDiagnosticsService> _mockDiagnosticsService = default!; // Initialize to suppress CS8618
        private FileSystemDocumentRepository _repository = default!; // Initialize to suppress CS8618

        private const string TestDocumentId = "testDocId123";

        [TestInitialize]
        public void Setup()
        {
            _mockMetadataStore = new Mock<IMetadataStore>();
            _mockContentStore = new Mock<IContentStore>();
            _mockVectorStore = new Mock<IVectorStore>(); // Added missing mock initialization

            // Corrected: Pass a valid DiagnosticsSettings object to the mock constructor
            _mockDiagnosticsService = new Mock<RagDiagnosticsService>(new DiagnosticsSettings());

            // FIXED: Updated constructor to include IVectorStore parameter
            _repository = new FileSystemDocumentRepository(
                _mockMetadataStore.Object,
                _mockContentStore.Object,
                _mockVectorStore.Object,
                _mockDiagnosticsService.Object);
        }

        [TestMethod]
        [Description("Tests that if content deletion fails, embeddings and metadata deletions are not attempted, and the exception is re-thrown.")]
        public async Task DeleteDocumentAsync_ContentDeletionFails_AbortsAndThrows()
        {
            // Arrange
            _mockContentStore.Setup(cs => cs.DeleteContentAsync(TestDocumentId))
                             .ThrowsAsync(new IOException("Simulated content delete failure"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<IOException>(async () =>
            {
                await _repository.DeleteDocumentAsync(TestDocumentId);
            }, "Expected IOException when content deletion fails.");

            // Verify
            _mockContentStore.Verify(cs => cs.DeleteContentAsync(TestDocumentId), Times.Once, "DeleteContentAsync should be called once.");
            _mockContentStore.Verify(cs => cs.DeleteEmbeddingsAsync(It.IsAny<string>()), Times.Never, "DeleteEmbeddingsAsync should not be called if content deletion fails.");
            _mockMetadataStore.Verify(ms => ms.DeleteMetadataAsync(It.IsAny<string>()), Times.Never, "DeleteMetadataAsync should not be called if content deletion fails.");

            // Verify diagnostics logging (example for critical failure)
            _mockDiagnosticsService.Verify(d => d.Log(DiagnosticLevel.Critical, It.IsAny<string>(), It.Is<string>(s => s.Contains("CRITICAL: Failed to delete content for document"))), Times.Once);

        }

        [TestMethod]
        [Description("Tests that if embeddings deletion fails, metadata deletion is not attempted, and the exception is re-thrown.")]
        public async Task DeleteDocumentAsync_EmbeddingsDeletionFails_AbortsMetadataDeletionAndThrows()
        {
            // Arrange
            _mockContentStore.Setup(cs => cs.DeleteContentAsync(TestDocumentId))
                             .Returns(Task.CompletedTask); // Content deletion succeeds

            _mockContentStore.Setup(cs => cs.DeleteEmbeddingsAsync(TestDocumentId))
                             .ThrowsAsync(new IOException("Simulated embeddings delete failure"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<IOException>(async () =>
            {
                await _repository.DeleteDocumentAsync(TestDocumentId);
            }, "Expected IOException when embeddings deletion fails.");

            // Verify
            _mockContentStore.Verify(cs => cs.DeleteContentAsync(TestDocumentId), Times.Once, "DeleteContentAsync should be called once.");
            _mockContentStore.Verify(cs => cs.DeleteEmbeddingsAsync(TestDocumentId), Times.Once, "DeleteEmbeddingsAsync should be called once.");
            _mockMetadataStore.Verify(ms => ms.DeleteMetadataAsync(It.IsAny<string>()), Times.Never, "DeleteMetadataAsync should not be called if embeddings deletion fails.");

            _mockDiagnosticsService.Verify(d => d.Log(DiagnosticLevel.Critical, It.IsAny<string>(), It.Is<string>(s => s.Contains("CRITICAL: Failed to delete embeddings for document"))), Times.Once);
        }

        [TestMethod]
        [Description("Tests that if metadata deletion fails, the exception is re-thrown.")]
        public async Task DeleteDocumentAsync_MetadataDeletionFails_Throws()
        {
            // Arrange
            _mockContentStore.Setup(cs => cs.DeleteContentAsync(TestDocumentId))
                             .Returns(Task.CompletedTask); // Content deletion succeeds
            _mockContentStore.Setup(cs => cs.DeleteEmbeddingsAsync(TestDocumentId))
                             .Returns(Task.CompletedTask); // Embeddings deletion succeeds

            var metadataException = new Exception("Simulated metadata delete failure"); // Can be a custom MetadataStoreException if defined
            _mockMetadataStore.Setup(ms => ms.DeleteMetadataAsync(TestDocumentId))
                              .ThrowsAsync(metadataException);

            // Act & Assert
            var actualException = await Assert.ThrowsExceptionAsync<Exception>(async () =>
            {
                await _repository.DeleteDocumentAsync(TestDocumentId);
            }, "Expected Exception when metadata deletion fails.");
            Assert.AreSame(metadataException, actualException, "The exception thrown should be the one from metadata store.");


            // Verify
            _mockContentStore.Verify(cs => cs.DeleteContentAsync(TestDocumentId), Times.Once);
            _mockContentStore.Verify(cs => cs.DeleteEmbeddingsAsync(TestDocumentId), Times.Once);
            _mockMetadataStore.Verify(ms => ms.DeleteMetadataAsync(TestDocumentId), Times.Once);

            _mockDiagnosticsService.Verify(d => d.Log(DiagnosticLevel.Error, It.IsAny<string>(), It.Is<string>(s => s.Contains("Error deleting metadata for document"))), Times.Once);
        }

        [TestMethod]
        [Description("Tests that successful deletion calls all underlying store methods.")]
        public async Task DeleteDocumentAsync_SuccessfulDeletion_CallsAllStores()
        {
            // Arrange
            _mockContentStore.Setup(cs => cs.DeleteContentAsync(TestDocumentId))
                             .Returns(Task.CompletedTask);
            _mockContentStore.Setup(cs => cs.DeleteEmbeddingsAsync(TestDocumentId))
                             .Returns(Task.CompletedTask);
            _mockMetadataStore.Setup(ms => ms.DeleteMetadataAsync(TestDocumentId))
                              .Returns(Task.CompletedTask);

            // Act
            await _repository.DeleteDocumentAsync(TestDocumentId);

            // Assert (Verifications)
            _mockContentStore.Verify(cs => cs.DeleteContentAsync(TestDocumentId), Times.Once);
            _mockContentStore.Verify(cs => cs.DeleteEmbeddingsAsync(TestDocumentId), Times.Once);
            _mockMetadataStore.Verify(ms => ms.DeleteMetadataAsync(TestDocumentId), Times.Once);

            _mockDiagnosticsService.Verify(d => d.Log(DiagnosticLevel.Info, It.IsAny<string>(), It.Is<string>(s => s.Contains("Successfully deleted all components for document"))), Times.Once);
        }
    }
}