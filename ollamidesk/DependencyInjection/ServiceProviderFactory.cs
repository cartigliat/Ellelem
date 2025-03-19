using System;
using Microsoft.Extensions.DependencyInjection;
using ollamidesk.Configuration;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Models;
using ollamidesk.RAG.Services;
using ollamidesk.RAG.ViewModels;
using ollamidesk.Services;

namespace ollamidesk.DependencyInjection
{
    /// <summary>
    /// Configures and provides access to the application's service provider
    /// </summary>
    public class ServiceProviderFactory
    {
        private static IServiceProvider? _serviceProvider;

        /// <summary>
        /// Gets whether the service provider has been initialized
        /// </summary>
        public static bool IsInitialized => _serviceProvider != null;

        /// <summary>
        /// Gets the service provider
        /// </summary>
        public static IServiceProvider ServiceProvider => _serviceProvider ?? throw new InvalidOperationException("Service provider not initialized");

        /// <summary>
        /// Initializes the service provider with configured services
        /// </summary>
        /// <param name="configFilePath">Optional path to the configuration file</param>
        public static void Initialize(string? configFilePath = null)
        {
            // Create a new service collection
            var services = new ServiceCollection();

            // Register configuration
            var configProvider = new ConfigurationProvider(configFilePath);
            var appSettings = configProvider.LoadConfiguration();
            services.AddSingleton(appSettings);
            services.AddSingleton(appSettings.Ollama);
            services.AddSingleton(appSettings.Rag);
            services.AddSingleton(appSettings.Storage);
            services.AddSingleton(appSettings.Diagnostics);
            services.AddSingleton(configProvider);

            // Register services
            RegisterServices(services, appSettings);

            // Build the service provider
            _serviceProvider = services.BuildServiceProvider();

            // Initialize diagnostics
            InitializeDiagnostics();

            // Log initialization
            var diagnostics = _serviceProvider.GetService<RagDiagnosticsService>();
            diagnostics?.Log(DiagnosticLevel.Info, "ServiceProviderFactory",
                "Dependency injection container initialized");
        }

        /// <summary>
        /// Registers services with the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="appSettings">The application settings</param>
        private static void RegisterServices(IServiceCollection services, AppSettings appSettings)
        {
            // Register utility services
            services.AddTransient<CommandLineService>();

            // Configure HttpClient using our configuration class
            HttpClientConfiguration.ConfigureHttpClients(services, appSettings);

            // Register repositories and services
            services.AddSingleton<IDocumentRepository, FileSystemDocumentRepository>();
            services.AddSingleton<IVectorStore, SqliteVectorStore>(); // Updated from FileSystemVectorStore
            services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
            services.AddSingleton<RagService>();

            // Register model factory and loader
            services.AddSingleton<OllamaModelFactory>();
            services.AddSingleton<OllamaModelLoader>();

            // Register default model
            services.AddTransient(sp =>
            {
                var factory = sp.GetRequiredService<OllamaModelFactory>();
                var settings = sp.GetRequiredService<OllamaSettings>();
                return factory.CreateModel(settings.DefaultModel);
            });

            // Register view models
            services.AddSingleton<DocumentViewModel>();
            services.AddSingleton<MainViewModel>();

            // Register UI components
            services.AddTransient<MainWindow>();
            services.AddTransient<SideMenuWindow>();
            services.AddTransient<RagDiagnosticWindow>();
            services.AddTransient<MainWindowRagHelper>();

            // Register diagnostics
            services.AddSingleton<RagDiagnosticsService>();
        }

        /// <summary>
        /// Initializes diagnostics components
        /// </summary>
        private static void InitializeDiagnostics()
        {
            var diagnosticsService = ServiceProvider.GetRequiredService<RagDiagnosticsService>();
            var settings = ServiceProvider.GetRequiredService<DiagnosticsSettings>();

            if (settings.EnableDiagnostics)
            {
                var level = Enum.Parse<DiagnosticLevel>(settings.DiagnosticLevel);
                diagnosticsService.Enable(level);
            }
        }

        /// <summary>
        /// Gets a service of type T from the service provider
        /// </summary>
        /// <typeparam name="T">The service type</typeparam>
        /// <returns>The service instance</returns>
        public static T GetService<T>() where T : class
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Service provider not initialized");
            }

            return ServiceProvider.GetService<T>() ?? throw new InvalidOperationException($"Service of type {typeof(T).Name} not registered");
        }
    }
}