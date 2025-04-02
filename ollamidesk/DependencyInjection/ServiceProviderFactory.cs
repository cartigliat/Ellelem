// ollamidesk/DependencyInjection/ServiceProviderFactory.cs
using System;
using Microsoft.Extensions.DependencyInjection;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.Services.Interfaces;
using ollamidesk.RAG.Services.Implementations;
using ollamidesk.RAG.ViewModels;
using ollamidesk.Services;
using ollamidesk.RAG.DocumentProcessors.Interfaces;
using ollamidesk.RAG.DocumentProcessors.Implementations;

namespace ollamidesk.DependencyInjection
{
    public static class ServiceProviderFactory
    {
        // ... properties ...
        private static IServiceProvider? _serviceProvider;
        public static bool IsInitialized => _serviceProvider != null;
        public static IServiceProvider ServiceProvider => _serviceProvider ?? throw new InvalidOperationException("Service provider not initialized.");


        public static void Initialize(string? configFilePath = null)
        {
            if (IsInitialized) return;

            var services = new ServiceCollection();
            var configProvider = new ConfigurationProvider(configFilePath);
            var appSettings = configProvider.LoadConfiguration();

            // Register Configurations
            services.AddSingleton(appSettings);
            services.AddSingleton(appSettings.Ollama);
            services.AddSingleton(appSettings.Rag);
            services.AddSingleton(appSettings.Storage); // Keep StorageSettings for paths
            services.AddSingleton(appSettings.Diagnostics);
            services.AddSingleton(configProvider);

            // Register Core Services (Diagnostics, Config)
            services.AddSingleton<RagDiagnosticsService>(); // Depends on DiagnosticsSettings
            services.AddSingleton<IRagConfigurationService, RagConfigurationService>(); // Depends on RagSettings, ConfigProvider, Diagnostics

            // Register Application Services
            RegisterServices(services, appSettings);

            _serviceProvider = services.BuildServiceProvider();

            // Initialize Database Provider after building ServiceProvider
            InitializeDatabaseProvider();

            InitializeDiagnostics(); // Initialize diagnostics logging level

            var diagnostics = _serviceProvider?.GetService<RagDiagnosticsService>();
            diagnostics?.Log(DiagnosticLevel.Info, "ServiceProviderFactory", "Dependency injection container initialized.");
        }

        private static void RegisterServices(IServiceCollection services, AppSettings appSettings)
        {
            services.AddTransient<CommandLineService>();
            HttpClientConfiguration.ConfigureHttpClients(services, appSettings);

            // --- API Client & Embedding Service ---
            services.AddSingleton<IOllamaApiClient, OllamaApiClient>(); // Uses IHttpClientFactory, OllamaSettings, Diagnostics
            services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>(); // Uses IHttpClientFactory, OllamaSettings, Diagnostics

            // --- Structure Extractors ---
            services.AddSingleton<IWordStructureExtractor, WordStructureExtractor>(); // Uses Diagnostics
            services.AddSingleton<IPdfStructureExtractor, PdfStructureExtractor>(); // Uses Diagnostics

            // --- Document Processors ---
            services.AddSingleton<IDocumentProcessor, TextDocumentProcessor>();
            services.AddSingleton<IDocumentProcessor, MarkdownDocumentProcessor>();
            services.AddSingleton<IDocumentProcessor, PdfDocumentProcessor>(); // Needs IPdfStructureExtractor
            services.AddSingleton<IDocumentProcessor, WordDocumentProcessor>(); // Needs IWordStructureExtractor
            services.AddSingleton<DocumentProcessorFactory>();

            // --- Chunking Strategies & Service ---
            services.AddSingleton<TextChunkingStrategy>(); // Uses Config, Diagnostics
            services.AddSingleton<CodeChunkingStrategy>(); // Uses Config, Diagnostics
            services.AddSingleton<StructuredChunkingStrategy>(); // Uses Config, Diagnostics, TextChunkingStrategy
            services.AddSingleton<IChunkingStrategy, TextChunkingStrategy>(sp => sp.GetRequiredService<TextChunkingStrategy>());
            services.AddSingleton<IChunkingStrategy, CodeChunkingStrategy>(sp => sp.GetRequiredService<CodeChunkingStrategy>());
            services.AddSingleton<IChunkingStrategy, StructuredChunkingStrategy>(sp => sp.GetRequiredService<StructuredChunkingStrategy>());
            services.AddSingleton<IChunkingService, ChunkingService>(); // Uses IEnumerable<IChunkingStrategy>, TextChunkingStrategy, Diagnostics

            // --- Storage Components ---
            services.AddSingleton<IMetadataStore, JsonMetadataStore>(); // Uses StorageSettings, Diagnostics
            services.AddSingleton<IContentStore, FileSystemContentStore>(); // Uses StorageSettings, Diagnostics
            services.AddSingleton<ISqliteConnectionProvider, SqliteConnectionProvider>(); // New: Uses StorageSettings, Diagnostics
            services.AddSingleton<IVectorStore, SqliteVectorStore>(); // Refactored: Uses ISqliteConnectionProvider, Diagnostics

            // --- RAG Core Services ---
            services.AddSingleton<IDocumentRepository, FileSystemDocumentRepository>(); // Uses IMetadataStore, IContentStore, Diagnostics
            services.AddSingleton<IDocumentManagementService, DocumentManagementService>(); // Uses IDocumentRepository, Diagnostics, DocProcessorFactory
            services.AddSingleton<IDocumentProcessingService, DocumentProcessingService>(); // Uses Repo, Embedding, VectorStore, Config, Diagnostics, Chunking
            services.AddSingleton<IRetrievalService, RetrievalService>(); // Uses VectorStore, Embedding, Config, Diagnostics
            services.AddSingleton<IPromptEngineeringService, PromptEngineeringService>(); // Uses Diagnostics

            // --- UI & Diagnostics UI Services ---
            services.AddSingleton<IDiagnosticsUIService, DiagnosticsUIService>();
            services.AddTransient<ShowDiagnosticsCommand>();

            // --- Model Factory & Default ---
            services.AddSingleton<OllamaModelFactory>(); // Uses Settings, Diagnostics, IOllamaApiClient
            services.AddTransient<IOllamaModel>(sp => sp.GetRequiredService<OllamaModelFactory>().CreateDefaultModel());

            // --- ViewModels & UI Components ---
            services.AddSingleton<DocumentViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddTransient<MainWindow>();
            services.AddTransient<SideMenuWindow>();
            services.AddTransient<RagDiagnosticWindow>();
        }

        // New method to initialize database provider after container is built
        private static void InitializeDatabaseProvider()
        {
            if (_serviceProvider == null)
            {
                Console.WriteLine("Error: Service provider not built before attempting to initialize database provider.");
                return;
            }
            try
            {
                var dbProvider = _serviceProvider.GetRequiredService<ISqliteConnectionProvider>();
                // Run initialization asynchronously but don't wait here (fire-and-forget pattern for startup)
                // Consider proper async handling if startup depends on DB being ready immediately.
                dbProvider.InitializeDatabaseAsync().ContinueWith(task => {
                    if (task.IsFaulted)
                    {
                        var diag = _serviceProvider.GetService<RagDiagnosticsService>();
                        diag?.Log(DiagnosticLevel.Critical, "ServiceProviderFactory", $"Background database initialization failed: {task.Exception?.InnerException?.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                // Log critical failure during provider retrieval or initial sync call attempt
                var diag = _serviceProvider.GetService<RagDiagnosticsService>();
                diag?.Log(DiagnosticLevel.Critical, "ServiceProviderFactory", $"Failed to get or initialize database provider: {ex.Message}");
                Console.WriteLine($"Critical Error: Failed to get or initialize database provider: {ex.Message}");
            }
        }

        // ... (InitializeDiagnostics and GetService methods remain the same) ...
        private static void InitializeDiagnostics()
        {
            if (_serviceProvider == null)
            {
                Console.WriteLine("Error: Service provider not built before attempting to initialize diagnostics.");
                return;
            }
            try
            {
                var diagnosticsService = _serviceProvider.GetRequiredService<RagDiagnosticsService>();
                var settings = _serviceProvider.GetRequiredService<DiagnosticsSettings>();
                if (settings.EnableDiagnostics)
                {
                    if (Enum.TryParse<DiagnosticLevel>(settings.DiagnosticLevel, true, out var level))
                    {
                        diagnosticsService.Enable(level);
                    }
                    else
                    {
                        diagnosticsService.Enable(DiagnosticLevel.Info);
                        diagnosticsService.Log(DiagnosticLevel.Warning, "ServiceProviderFactory", $"Invalid DiagnosticLevel '{settings.DiagnosticLevel}' in config. Defaulting to Info.");
                    }
                }
            }
            catch (Exception ex)
            {
                var diagnosticsService = _serviceProvider?.GetService<RagDiagnosticsService>();
                diagnosticsService?.Log(DiagnosticLevel.Critical, "ServiceProviderFactory", $"Failed to initialize diagnostics: {ex.Message}");
                Console.WriteLine($"Critical Error: Failed to initialize diagnostics: {ex.Message}");
            }
        }

        public static T GetService<T>() where T : notnull
        {
            if (!IsInitialized || _serviceProvider == null) throw new InvalidOperationException("Service provider not initialized.");
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}