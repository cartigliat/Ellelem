using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ollamidesk.DependencyInjection;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private bool _isShuttingDown = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize dependency injection with default config file path
        ServiceProviderFactory.Initialize();

        // Add global exception handling
        Current.DispatcherUnhandledException += (s, args) =>
        {
            // Log the exception using RagDiagnostics
            var diagnostics = ServiceProviderFactory.GetService<RagDiagnosticsService>();
            diagnostics.Log(DiagnosticLevel.Error, "Application", $"Unhandled exception: {args.Exception.Message}");

            MessageBox.Show($"An unhandled exception occurred: {args.Exception.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Add session ending handler for Windows shutdown/logoff
        SessionEnding += OnSessionEnding;

        // Create and show main window from the service provider
        var mainWindow = ServiceProviderFactory.GetService<MainWindow>();
        mainWindow.Show();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Handle Windows shutdown/logoff
        if (!_isShuttingDown)
        {
            _isShuttingDown = true;
            CleanupServices();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_isShuttingDown)
        {
            _isShuttingDown = true;
            CleanupServices();
        }

        base.OnExit(e);
    }

    private void CleanupServices()
    {
        RagDiagnosticsService? diagnostics = null;

        try
        {
            diagnostics = ServiceProviderFactory.GetService<RagDiagnosticsService>();
            diagnostics.Log(DiagnosticLevel.Info, "Application", "Starting application shutdown cleanup");

            // 1. Save any pending configuration changes first
            SavePendingChanges(diagnostics);

            // 2. Cleanup UI-related services
            CleanupUIServices(diagnostics);

            // 3. Cleanup RAG services
            CleanupRagServices(diagnostics);

            // 4. Cleanup API and HTTP services
            CleanupApiServices(diagnostics);

            // 5. Cleanup storage services
            CleanupStorageServices(diagnostics);

            // 6. Final diagnostics cleanup
            diagnostics.Log(DiagnosticLevel.Info, "Application", "Application shutdown cleanup completed");

            // 7. Dispose the entire service provider
            ServiceProviderFactory.Dispose();
        }
        catch (Exception ex)
        {
            // Log error but don't throw during shutdown
            diagnostics?.Log(DiagnosticLevel.Error, "Application", $"Error during cleanup: {ex.Message}");

            // Fallback logging if diagnostics fails
            System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");

            // Still try to dispose the service provider even if cleanup failed
            try
            {
                ServiceProviderFactory.Dispose();
            }
            catch (Exception disposeEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing service provider: {disposeEx.Message}");
            }
        }
    }

    private void SavePendingChanges(RagDiagnosticsService diagnostics)
    {
        try
        {
            // Save any pending configuration changes before shutdown
            ServiceProviderFactory.SavePendingChanges();
            diagnostics.Log(DiagnosticLevel.Debug, "Application", "Pending changes saved");
        }
        catch (Exception ex)
        {
            diagnostics.Log(DiagnosticLevel.Warning, "Application", $"Error saving pending changes: {ex.Message}");
        }
    }

    private void CleanupUIServices(RagDiagnosticsService diagnostics)
    {
        try
        {
            // Cleanup diagnostics UI service
            var diagnosticsUIService = ServiceProviderFactory.ServiceProvider.GetService<IDiagnosticsUIService>();
            diagnosticsUIService?.UnregisterHandlers();

            diagnostics.Log(DiagnosticLevel.Debug, "Application", "UI services cleaned up");
        }
        catch (Exception ex)
        {
            diagnostics.Log(DiagnosticLevel.Warning, "Application", $"Error cleaning up UI services: {ex.Message}");
        }
    }

    private void CleanupRagServices(RagDiagnosticsService diagnostics)
    {
        try
        {
            // Note: Individual service disposal is now handled by ServiceProviderFactory.Dispose()
            // We just need to signal any ongoing operations to stop gracefully

            // Stop any ongoing document processing
            var docProcessingService = ServiceProviderFactory.ServiceProvider.GetService<IDocumentProcessingService>();
            // Note: If you add cancellation tokens to processing operations, cancel them here

            // The actual disposal of vector store, content store, and SQLite provider
            // will be handled automatically by the ServiceProvider disposal

            diagnostics.Log(DiagnosticLevel.Debug, "Application", "RAG services cleaned up");
        }
        catch (Exception ex)
        {
            diagnostics.Log(DiagnosticLevel.Warning, "Application", $"Error cleaning up RAG services: {ex.Message}");
        }
    }

    private void CleanupApiServices(RagDiagnosticsService diagnostics)
    {
        try
        {
            // Note: Individual API service disposal is now handled by ServiceProviderFactory.Dispose()
            // We just need to signal any ongoing operations to stop gracefully

            // Cancel any ongoing API requests if possible
            // (This would require implementing cancellation token support in your API services)

            diagnostics.Log(DiagnosticLevel.Debug, "Application", "API services cleaned up");
        }
        catch (Exception ex)
        {
            diagnostics.Log(DiagnosticLevel.Warning, "Application", $"Error cleaning up API services: {ex.Message}");
        }
    }

    private void CleanupStorageServices(RagDiagnosticsService diagnostics)
    {
        try
        {
            // Note: Configuration saving is now handled in SavePendingChanges()
            // Storage service disposal is handled by ServiceProviderFactory.Dispose()

            diagnostics.Log(DiagnosticLevel.Debug, "Application", "Storage services will be cleaned up by service provider disposal");
        }
        catch (Exception ex)
        {
            diagnostics.Log(DiagnosticLevel.Warning, "Application", $"Error cleaning up storage services: {ex.Message}");
        }
    }
}