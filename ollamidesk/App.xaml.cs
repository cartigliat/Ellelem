using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ollamidesk.DependencyInjection;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
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

        // Create and show main window from the service provider
        var mainWindow = ServiceProviderFactory.GetService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up resources 
        var diagnostics = ServiceProviderFactory.GetService<RagDiagnosticsService>();
        diagnostics.Log(DiagnosticLevel.Info, "Application", "Application shutting down");

        base.OnExit(e);
    }
}