using System;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.Transition;

namespace ollamidesk
{
    public static class OllamaModelLoader
    {
        public static IOllamaModel LoadModel(string modelName)
        {
            var settings = LegacySupport.CreateOllamaSettings();
            var diagnostics = LegacySupport.CreateDiagnosticsService();
            return new OllamaModel(modelName, settings, diagnostics);
        }
    }
}