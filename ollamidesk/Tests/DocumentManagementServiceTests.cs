// ollamidesk/Tests/DocumentManagementServiceTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq; // Assuming Moq is available for mocking
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.DocumentProcessors.Implementations; // For DocumentProcessorFactory
using System;
using System.IO; // For IOException or other specific exceptions
using System.Threading.Tasks;

namespace ollamidesk.Tests.RAG.Services
{
    // Define a custom exception for testing propagation
    public class RepositoryDeleteException : Exception
    {
        public RepositoryDeleteException(string message) : base(message) { }
    }

    [TestClass]
    public class DocumentManagementServiceTests
    {
        private Mock<IDocumentRepository> _mockDocumentRepository;
        private Mock<RagDiagnosticsService> _mockDiagnosticsService;
        private Mock<DocumentProcessorFactory> _mockDocumentProcessorFactory;
        private DocumentManagementService _service;

        private const string TestDocumentId = "docTestId456";

        [TestInitialize]
        public void Setup()
        {
            _mockDocumentRepository = new Mock<IDocumentRepository>();

            // Assuming RagDiagnosticsService can be mocked like this.
            // Adjust if its constructor requires specific non-mockable parameters.
            _mockDiagnosticsService = new Mock<RagDiagnosticsService>(null, null);

            // DocumentProcessorFactory might also need specific setup if its constructor is complex.
            // For this test, its behavior isn't critical for delete, so basic mock is fine.
            _mockDocumentProcessorFactory = new Mock<DocumentProcessorFactory>(null); // Assuming it takes IServiceProvider or similar

            _service = new DocumentManagementService(
                _mockDocumentRepository.Object,
                _mockDiagnosticsService.Object,
                _mockDocumentProcessorFactory.Object);
        }

        [TestMethod]
        [Description("Tests that if the repository throws an exception during delete, the service propagates it and logs an error.")]
        public async Task DeleteDocumentAsync_RepositoryThrowsException_PropagatesException()
        {
            // Arrange
            var repositoryException = new RepositoryDeleteException("Simulated repository delete error");
            _mockDocumentRepository.Setup(repo => repo.DeleteDocumentAsync(TestDocumentId))
                                   .ThrowsAsync(repositoryException);

            // Act & Assert
            var caughtException = await Assert.ThrowsExceptionAsync<RepositoryDeleteException>(async () =>
            {
                await _service.DeleteDocumentAsync(TestDocumentId);
            }, "Expected RepositoryDeleteException to be propagated.");

            Assert.AreSame(repositoryException, caughtException, "The propagated exception should be the same instance as thrown by the repository.");

            // Verify logging
            _mockDiagnosticsService.Verify(diag => diag.Log(
                DiagnosticLevel.Error,
                "DocumentManagementService",
                It.Is<string>(s => s.Contains($"Failed to delete document: {repositoryException.Message}"))
            ), Times.Once, "An error diagnostic should be logged.");

            // Verify that StartOperation and EndOperation are called around the attempt
            _mockDiagnosticsService.Verify(diag => diag.StartOperation("DeleteDocument"), Times.Once);
            _mockDiagnosticsService.Verify(diag => diag.EndOperation("DeleteDocument"), Times.Once);
        }

        [TestMethod]
        [Description("Tests that a successful deletion calls the repository and logs informational messages.")]
        public async Task DeleteDocumentAsync_SuccessfulDeletion_CallsRepositoryAndLogs()
        {
            // Arrange
            _mockDocumentRepository.Setup(repo => repo.DeleteDocumentAsync(TestDocumentId))
                                   .Returns(Task.CompletedTask);

            // Act
            await _service.DeleteDocumentAsync(TestDocumentId);

            // Assert (Verify interactions and logging)
            _mockDocumentRepository.Verify(repo => repo.DeleteDocumentAsync(TestDocumentId), Times.Once,
                "DeleteDocumentAsync on the repository should be called exactly once.");

            // Verify informational logging for starting deletion
            _mockDiagnosticsService.Verify(diag => diag.Log(
                DiagnosticLevel.Info,
                "DocumentManagementService",
                $"Deleting document: {TestDocumentId}"
            ), Times.Once, "An info diagnostic for starting deletion should be logged.");

            // Verify informational logging for successful deletion
            _mockDiagnosticsService.Verify(diag => diag.Log(
                DiagnosticLevel.Info,
                "DocumentManagementService",
                $"Document deleted: {TestDocumentId}"
            ), Times.Once, "An info diagnostic for successful deletion should be logged.");

            // Verify that StartOperation and EndOperation are called
            _mockDiagnosticsService.Verify(diag => diag.StartOperation("DeleteDocument"), Times.Once);
            _mockDiagnosticsService.Verify(diag => diag.EndOperation("DeleteDocument"), Times.Once);
        }
    }
}
