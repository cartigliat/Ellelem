using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.Transition;

namespace ollamidesk
{
    /// <summary>
    /// Helper class to integrate RAG diagnostics into the main window
    /// </summary>
    public class MainWindowRagHelper
    {
        private readonly MainWindow _mainWindow;
        private readonly RagDiagnosticsService _diagnostics;
        private RagDiagnosticWindow? _diagnosticWindow;
        private MenuItem? _ragDiagnosticsMenuItem;

        public MainWindowRagHelper(MainWindow mainWindow, RagDiagnosticsService diagnostics)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            // Create UI integration
            AddDiagnosticsMenu();

            // Add keyboard shortcut
            _mainWindow.KeyDown += MainWindow_KeyDown;

            _diagnostics.Log(DiagnosticLevel.Info, "MainWindowRagHelper", "Initialized RAG helper");
        }

        private void AddDiagnosticsMenu()
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

                    // Add RAG Diagnostics menu item
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
                    // If menu not found, try to add a button
                    AddDiagnosticsButton();
                }
            }
            catch (Exception ex)
            {
                // If we can't modify the UI, log the error but don't crash
                _diagnostics.Log(DiagnosticLevel.Error, "MainWindowRagHelper",
                    $"Error adding diagnostics menu: {ex.Message}");
            }
        }

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
                // If we can't modify the UI, log the error but don't crash
                _diagnostics.Log(DiagnosticLevel.Error, "MainWindowRagHelper",
                    $"Error adding diagnostics button: {ex.Message}");
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Shift+D shortcut for diagnostics window
            if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                ShowDiagnosticsWindow();
                e.Handled = true;
            }
        }

        public void ShowDiagnosticsWindow()
        {
            if (_diagnosticWindow == null || !_diagnosticWindow.IsVisible)
            {
                _diagnosticWindow = new RagDiagnosticWindow(_diagnostics);
                _diagnosticWindow.Owner = _mainWindow;
                _diagnosticWindow.Show();

                _diagnostics.Log(DiagnosticLevel.Info, "MainWindowRagHelper", "Opened diagnostics window");
            }
            else
            {
                _diagnosticWindow.Activate();
            }
        }

        public void Cleanup()
        {
            // Remove event handlers
            _mainWindow.KeyDown -= MainWindow_KeyDown;

            // Close diagnostic window if open
            _diagnosticWindow?.Close();

            _diagnostics.Log(DiagnosticLevel.Info, "MainWindowRagHelper", "Cleaned up resources");
        }
    }

    /// <summary>
    /// Extension to MainWindow to easily add RAG diagnostics
    /// </summary>
    public static class MainWindowExtensions
    {
        // Legacy method for backward compatibility
        public static MainWindowRagHelper EnableRagDiagnostics(this MainWindow mainWindow)
        {
            var diagnostics = LegacySupport.CreateDiagnosticsService();
            return new MainWindowRagHelper(mainWindow, diagnostics);
        }

        // New method with DI
        public static MainWindowRagHelper EnableRagDiagnostics(this MainWindow mainWindow, RagDiagnosticsService diagnostics)
        {
            return new MainWindowRagHelper(mainWindow, diagnostics);
        }
    }
}