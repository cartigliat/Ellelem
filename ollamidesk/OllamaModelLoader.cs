using System;

namespace ollamidesk
{
    public static class OllamaModelLoader
    {
        public static IOllamaModel LoadModel(string modelName)
        {
            return new OllamaModel(modelName);
        }
    }
}