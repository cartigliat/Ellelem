using System;

namespace ollamidesk.RAG.Exceptions
{
    /// <summary>
    /// Exception thrown when an error occurs during document processing
    /// </summary>
    public class DocumentProcessingException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the DocumentProcessingException class
        /// </summary>
        /// <param name="message">The error message</param>
        public DocumentProcessingException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the DocumentProcessingException class with an inner exception
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="innerException">The inner exception that caused this exception</param>
        public DocumentProcessingException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}