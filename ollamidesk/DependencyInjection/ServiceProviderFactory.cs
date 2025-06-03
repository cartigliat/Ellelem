// ollamidesk/DependencyInjection/ServiceProviderFactory.cs
// Enhanced with proper disposal support
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
        private static IServiceProvider? _serviceProvider;
        private static bool _isDisposing = false;

        public static bool IsInitialized => _serviceProvider != null && !_isDisposing;
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
            services.AddSingleton(appSettings.Storage);
            services.AddSingleton(appSettings.Diagnostics);
            services.AddSingleton(configProvider);

            // Register Core Services (Diagnostics, Config)
            services.AddSingleton<RagDiagnosticsService>();
            services.AddSingleton<IRagConfigurationService, RagConfigurationService>();

            // Register Application Services
            RegisterServices(services, appSettings);

            _serviceProvider = services.BuildServiceProvider();

            // Initialize Database Provider after building ServiceProvider
            InitializeDatabaseProvider();

            InitializeDiagnostics();

            var diagnostics = _serviceProvider?.GetService<RagDiagnosticsService>();
            diagnostics?.Log(DiagnosticLevel.Info, "ServiceProviderFactory", "Dependency injection container initialized.");
        }

        private static void RegisterServices(IServiceCollection services, AppSettings appSettings)
        {
            services.AddTransient<CommandLineService>();
            HttpClientConfiguration.ConfigureHttpClients(services, appSettings);

            // --- API Client & Embedding Service ---
            services.AddSingleton<IOllamaApiClient, OllamaApiClient>();
            services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();

            // --- Structure Extractors ---
            services.AddSingleton<IWordStructureExtractor, WordStructureExtractor>();
            services.AddSingleton<IPdfStructureExtractor, PdfStructureExtractor>();

            // --- Document Processors ---
            services.AddSingleton<IDocumentProcessor, TextDocumentProcessor>();
            services.AddSingleton<IDocumentProcessor, MarkdownDocumentProcessor>();
            services.AddSingleton<IDocumentProcessor, PdfDocumentProcessor>();
            services.AddSingleton<IDocumentProcessor, WordDocumentProcessor>();
            services.AddSingleton<DocumentProcessorFactory>();

            // --- Chunking Strategies & Service ---
            services.AddSingleton<TextChunkingStrategy>();
            services.AddSingleton<CodeChunkingStrategy>();
            services.AddSingleton<StructuredChunkingStrategy>();
            services.AddSingleton<HierarchicalChunkingStrategy>();

            // Register strategies for IEnumerable<IChunkingStrategy>
            services.AddSingleton<IChunkingStrategy, HierarchicalChunkingStrategy>(sp => sp.GetRequiredService<HierarchicalChunkingStrategy>());
            services.AddSingleton<IChunkingStrategy, CodeChunkingStrategy>(sp => sp.GetRequiredService<CodeChunkingStrategy>());
            services.AddSingleton<IChunkingStrategy, StructuredChunkingStrategy>(sp => sp.GetRequiredService<StructuredChunkingStrategy>());
            services.AddSingleton<IChunkingStrategy, TextChunkingStrategy>(sp => sp.GetRequiredService<TextChunkingStrategy>());

            services.AddSingleton<IChunkingService, ChunkingService>();

            // --- Storage Components ---
            services.AddSingleton<IMetadataStore, JsonMetadataStore>();
            services.AddSingleton<IContentStore, FileSystemContentStore>();
            services.AddSingleton<ISqliteConnectionProvider, SqliteConnectionProvider>();
            services.AddSingleton<IVectorStore, SqliteVectorStore>();

            // --- RAG Core Services ---
            // ONLY CHANGE: Updated comment to reflect IVectorStore dependency
            services.AddSingleton<IDocumentRepository, FileSystemDocumentRepository>(); // Uses IMetadataStore, IContentStore, IVectorStore, Diagnostics
            services.AddSingleton<IDocumentManagementService, DocumentManagementService>();
            services.AddSingleton<IDocumentProcessingService, DocumentProcessingService>();
            services.AddSingleton<IRetrievalService, RetrievalService>();
            services.AddSingleton<IPromptEngineeringService, PromptEngineeringService>();

            // --- UI & Diagnostics UI Services ---
            services.AddSingleton<IDiagnosticsUIService, DiagnosticsUIService>();
            services.AddTransient<ShowDiagnosticsCommand>();

            // --- Model Factory & Default ---
            services.AddSingleton<OllamaModelFactory>();
            services.AddTransient<IOllamaModel>(sp => sp.GetRequiredService<OllamaModelFactory>().CreateDefaultModel());

            // --- ViewModels & UI Components ---
            services.AddSingleton<DocumentViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddTransient<MainWindow>();
            services.AddTransient<SideMenuWindow>();
            services.AddTransient<RagDiagnosticWindow>();
            services.AddTransient<ollamidesk.RAG.Windows.RagSettingsWindow>();
        }

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
                var diag = _serviceProvider.GetService<RagDiagnosticsService>();
                diag?.Log(DiagnosticLevel.Critical, "ServiceProviderFactory", $"Failed to get or initialize database provider: {ex.Message}");
                Console.WriteLine($"Critical Error: Failed to get or initialize database provider: {ex.Message}");
            }
        }

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

        /// <summary>
        /// Safely disposes of all services and resources. Call this during application shutdown.
        /// </summary>
        public static void Dispose()
        {
            if (_isDisposing || _serviceProvider == null) return;

            _isDisposing = true;

            try
            {
                var diagnostics = _serviceProvider.GetService<RagDiagnosticsService>();
                diagnostics?.Log(DiagnosticLevel.Info, "ServiceProviderFactory", "Starting service provider disposal");

                // Dispose of the service provider - this will dispose all registered services that implement IDisposable
                if (_serviceProvider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }

                _serviceProvider = null;

                // Log final message to console since diagnostics may be disposed
                Console.WriteLine("Service provider disposed successfully");
            }
            catch (Exception ex)
            {
                // Log to console since diagnostics may be disposed
                Console.WriteLine($"Error during service provider disposal: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error during service provider disposal: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to save any pending configuration changes before shutdown
        /// </summary>
        public static void SavePendingChanges()
        {
            if (!IsInitialized) return;

            try
            {
                var diagnostics = _serviceProvider?.GetService<RagDiagnosticsService>();
                diagnostics?.Log(DiagnosticLevel.Info, "ServiceProviderFactory", "Saving pending configuration changes");

                var configService = _serviceProvider?.GetService<IRagConfigurationService>();
                configService?.SaveConfigurationAsync().Wait(TimeSpan.FromSeconds(5)); // Wait up to 5 seconds

                diagnostics?.Log(DiagnosticLevel.Info, "ServiceProviderFactory", "Configuration changes saved");
            }
            catch (Exception ex)
            {
                var diagnostics = _serviceProvider?.GetService<RagDiagnosticsService>();
                diagnostics?.Log(DiagnosticLevel.Warning, "ServiceProviderFactory", $"Error saving pending changes: {ex.Message}");
            }
        }
    }
}