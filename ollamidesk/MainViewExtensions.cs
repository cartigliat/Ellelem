// MainViewExtensions.cs
using System;
using ollamidesk.RAG;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.ViewModels;

namespace ollamidesk
{
    public static class ViewModelExtensions
    {
        /// <summary>
        /// Gets the RAG service from a view model
        /// </summary>
        /// <param name="viewModel">The view model to get the RAG service from</param>
        /// <returns>The RAG service</returns>
        public static RagService GetRagService(this DocumentViewModel viewModel)
        {
            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));

            if (viewModel is IRagProvider ragProvider)
            {
                return ragProvider.GetRagService();
            }

            throw new InvalidOperationException("ViewMoodel does not implement IRagProvider");
        }
    }
}