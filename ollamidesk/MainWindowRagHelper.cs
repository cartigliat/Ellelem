// ollamidesk/MainWindowRagHelper.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ollamidesk.DependencyInjection;
using ollamidesk.RAG.Diagnostics;
// Removed using directives for ViewModels/Windows as ShowRagSettingsWindow is removed
// using ollamidesk.RAG.ViewModels;
// using ollamidesk.RAG.Windows;

namespace ollamidesk
{
    /// <summary>
    /// Helper class to integrate RAG diagnostics into the main window
    /// (Settings integration moved to DocumentViewModel/MainWindow.xaml)
    /// </summary>
    public class MainWindowRagHelper
    {
        private readonly MainWindow _mainWindow;
        private readonly RagDiagnosticsService _diagnostics;
        private RagDiagnosticWindow? _diagnosticWindow;
        private MenuItem? _ragDiagnosticsMenuItem;
        // --- REMOVED RAG Settings Menu Item Fields ---
        // private MenuItem? _ragSettingsMenuItem;
        // public MenuItem? RagSettingsMenuItem => _ragSettingsMenuItem;
        // -------------

        public MainWindowRagHelper(MainWindow mainWindow, RagDiagnosticsService diagnostics)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            // Create UI integration (Only Diagnostics now)
            AddMenus(); // Renamed for clarity

            // Add keyboard shortcut (for diagnostics)
            _mainWindow.KeyDown += MainWindow_KeyDown;

            _diagnostics.Log(DiagnosticLevel.Info, "MainWindowRagHelper", "Initialized RAG helper (Diagnostics only)");
        }

        // Method now only handles Diagnostics menu item
        private void AddMenus()
        {
            try
            {
                // Find the main menu
                if (_mainWindow.FindName("MainMenu") is Menu mainMenu)
                {
                    // Create or find Tools menu
                    MenuItem? toolsMenu = null;

                    foreach (var item in mainMenu.Items)
                    {
                        if (item is MenuItem menuItem && menuItem.Header.ToString() == "Tools")
                        {
                            toolsMenu = menuItem;
                            break;
                        }
                    }

                    if (toolsMenu == null)
                    {
                        toolsMenu = new MenuItem { Header = "Tools" };
                        mainMenu.Items.Add(toolsMenu);
                    }

                    // --- REMOVED RAG Settings menu item ---
                    // _ragSettingsMenuItem = new MenuItem { ... };
                    // _ragSettingsMenuItem.Click += ...;
                    // toolsMenu.Items.Add(_ragSettingsMenuItem);
                    // ------------------------------------

                    // Add RAG Diagnostics menu item (Keep this)
                    _ragDiagnosticsMenuItem = new MenuItem
                    {
                        Header = "RAG Diagnostics",
                        InputGestureText = "Ctrl+Shift+D"
                    };
                    _ragDiagnosticsMenuItem.Click += (s, e) => ShowDiagnosticsWindow();
                    toolsMenu.Items.Add(_ragDiagnosticsMenuItem);
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Warning, "MainWindowRagHelper", "MainMenu control not found. Cannot add Tools menu items.");
                    // If menu not found, try to add buttons (existing logic)
                    AddDiagnosticsButton(); // Keep trying to add button as fallback
                }
            }
            catch (Exception ex)
            {
                // If we can't modify the UI, log the error but don't crash
                _diagnostics.Log(DiagnosticLevel.Error, "MainWindowRagHelper",
                    $"Error adding menu items: {ex.Message}");
            }
        }

        // Existing AddDiagnosticsButton method remains the same...
        private void AddDiagnosticsButton()
        {
            try
            {
                // Try to find a toolbar or other container to add a button
                if (_mainWindow.FindName("MainToolBar") is ToolBar toolBar)
                {
                    var button = new Button
                    {
                        Content = "RAG Diagnostics",
                        ToolTip = "Open RAG Diagnostics Window (Ctrl+Shift+D)"
                    };

                    button.Click += (s, e) => ShowDiagnosticsWindow();
                    toolBar.Items.Add(button);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "MainWindowRagHelper",
                    $"Error adding diagnostics button: {ex.Message}");
            }
        }


        // Existing MainWindow_KeyDown for diagnostics shortcut remains the same...
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                ShowDiagnosticsWindow();
                e.Handled = true;
            }
        }

        // Existing ShowDiagnosticsWindow remains the same...
        public void ShowDiagnosticsWindow()
        {
            if (_diagnosticWindow == null || !_diagnosticWindow.IsVisible)
            {
                try
                {
                    _diagnosticWindow = ServiceProviderFactory.GetService<RagDiagnosticWindow>();
                    _diagnosticWindow.Owner = _mainWindow;
                    _diagnosticWindow.Show();
                    _diagnostics.Log(DiagnosticLevel.Info, "MainWindowRagHelper", "Opened diagnostics window");
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Error, "MainWindowRagHelper", $"Failed to open diagnostics window: {ex.Message}");
                    MessageBox.Show("Could not open RAG Diagnostics window.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                _diagnosticWindow.Activate();
            }
        }

        // --- REMOVED Method to show RAG Settings Window ---
        // public void ShowRagSettingsWindow() { ... }
        // ---

        // Existing Cleanup method remains the same...
        public void Cleanup()
        {
            _mainWindow.KeyDown -= MainWindow_KeyDown;
            _diagnosticWindow?.Close();
            _diagnostics.Log(DiagnosticLevel.Info, "MainWindowRagHelper", "Cleaned up resources");
        }
    }
}