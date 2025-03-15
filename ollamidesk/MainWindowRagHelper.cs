using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk
{
    /// <summary>
    /// Helper class to integrate RAG diagnostics into the main window
    /// </summary>
    public class MainWindowRagHelper
    {
        private readonly MainWindow _mainWindow;
        private RagDiagnosticWindow? _diagnosticWindow;
        private MenuItem? _ragDiagnosticsMenuItem;

        public MainWindowRagHelper(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

            // Initialize diagnostics system
            RagDiagnostics.Instance.Enable(DiagnosticLevel.Info);

            // Create UI integration
            AddDiagnosticsMenu();

            // Add keyboard shortcut
            _mainWindow.KeyDown += MainWindow_KeyDown;
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
                RagDiagnostics.Instance.Log(DiagnosticLevel.Error, "MainWindowRagHelper",
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
                RagDiagnostics.Instance.Log(DiagnosticLevel.Error, "MainWindowRagHelper",
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
                _diagnosticWindow = new RagDiagnosticWindow();
                _diagnosticWindow.Owner = _mainWindow;
                _diagnosticWindow.Show();
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

            // Disable diagnostics
            RagDiagnostics.Instance.Disable();
        }
    }

    /// <summary>
    /// Extension to MainWindow to easily add RAG diagnostics
    /// </summary>
    public static class MainWindowExtensions
    {
        public static MainWindowRagHelper EnableRagDiagnostics(this MainWindow mainWindow)
        {
            return new MainWindowRagHelper(mainWindow);
        }
    }
}