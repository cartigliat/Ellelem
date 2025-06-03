using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.DocumentProcessors.Implementations;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.Configuration;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ollamidesk.Tests.RAG.Services
{
    public class RepositoryDeleteException : Exception
    {
        public RepositoryDeleteException(string message) : base(message) { }
    }

    [TestClass]
    public class DocumentManagementServiceTests
    {
        private Mock<IDocumentRepository> _mockDocumentRepository = default!;
        private Mock<RagDiagnosticsService> _mockDiagnosticsService = default!;
        private Mock<DocumentProcessorFactory> _mockDocumentProcessorFactory = default!;
        private DocumentManagementService _service = default!;

        private const string TestDocumentId = "testDocId456";

        [TestInitialize]
        public void Setup()
        {
            _mockDocumentRepository = new Mock<IDocumentRepository>();

            _mockDiagnosticsService = new Mock<RagDiagnosticsService>(new DiagnosticsSettings());

            var mockTextProcessor = new Mock<IDocumentProcessor>();
            mockTextProcessor.Setup(p => p.CanProcess(".txt")).Returns(true);
            mockTextProcessor.Setup(p => p.SupportedExtensions).Returns(new[] { ".txt" });
            mockTextProcessor.Setup(p => p.ExtractTextAsync(It.IsAny<string>())).ReturnsAsync("mock text content");
            mockTextProcessor.Setup(p => p.SupportsStructuredExtraction).Returns(true);
            mockTextProcessor.Setup(p => p.ExtractStructuredContentAsync(It.IsAny<string>())).ReturnsAsync(new StructuredDocument());

            var processors = new List<IDocumentProcessor> { mockTextProcessor.Object };

            _mockDocumentProcessorFactory = new Mock<DocumentProcessorFactory>(processors, _mockDiagnosticsService.Object);

            _service = new DocumentManagementService(
                _mockDocumentRepository.Object,
                _mockDiagnosticsService.Object,
                _mockDocumentProcessorFactory.Object);
        }

        [TestMethod]
        [Description("Tests that if the repository throws an exception during delete, the service propagates it and logs an error.")]
        public async Task DeleteDocumentAsync_RepositoryThrowsException_PropagatesException()
        {
            var repositoryException = new RepositoryDeleteException("Simulated repository delete error");
            _mockDocumentRepository.Setup(repo => repo.DeleteDocumentAsync(TestDocumentId))
                                   .ThrowsAsync(repositoryException);

            var caughtException = await Assert.ThrowsExceptionAsync<RepositoryDeleteException>(async () =>
            {
                await _service.DeleteDocumentAsync(TestDocumentId);
            }, "Expected RepositoryDeleteException to be propagated.");

            Assert.AreSame(repositoryException, caughtException, "The propagated exception should be the same instance as thrown by the repository.");

            _mockDiagnosticsService.Verify(diag => diag.Log(
                DiagnosticLevel.Error,
                "DocumentManagementService",
                It.Is<string>(s => s.Contains($"Failed to delete document: {repositoryException.Message}"))
            ), Times.Once, "An error diagnostic should be logged.");

            _mockDiagnosticsService.Verify(diag => diag.StartOperation("DeleteDocument"), Times.Once);
            _mockDiagnosticsService.Verify(diag => diag.EndOperation("DeleteDocument"), Times.Once);
        }

        [TestMethod]
        [Description("Tests that a successful deletion calls the repository and logs informational messages.")]
        public async Task DeleteDocumentAsync_SuccessfulDeletion_CallsRepositoryAndLogs()
        {
            _mockDocumentRepository.Setup(repo => repo.DeleteDocumentAsync(TestDocumentId))
                                   .Returns(Task.CompletedTask);

            await _service.DeleteDocumentAsync(TestDocumentId);

            _mockDocumentRepository.Verify(repo => repo.DeleteDocumentAsync(TestDocumentId), Times.Once,
                "DeleteDocumentAsync on the repository should be called exactly once.");

            _mockDiagnosticsService.Verify(diag => diag.Log(
                DiagnosticLevel.Info,
                "DocumentManagementService",
                $"Deleting document: {TestDocumentId}"
            ), Times.Once, "An info diagnostic for starting deletion should be logged.");

            _mockDiagnosticsService.Verify(diag => diag.Log(
                DiagnosticLevel.Info,
                "DocumentManagementService",
                $"Document deleted: {TestDocumentId}"
            ), Times.Once, "An info diagnostic for successful deletion should be logged.");

            _mockDiagnosticsService.Verify(diag => diag.StartOperation("DeleteDocument"), Times.Once);
            _mockDiagnosticsService.Verify(diag => diag.EndOperation("DeleteDocument"), Times.Once);
        }
    }
}