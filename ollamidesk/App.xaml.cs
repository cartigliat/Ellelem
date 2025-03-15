using System.Configuration;
using System.Data;
using System.Windows;

namespace ollamidesk;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Add global exception handling
        Current.DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"An unhandled exception occurred: {args.Exception.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}