using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ollamidesk.Common.MVVM;
using ollamidesk.DependencyInjection;
using ollamidesk.RAG.Diagnostics;
using ollamidesk.RAG.Services.Interfaces;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Implementation of the diagnostics UI service that decouples UI operations from diagnostic functionality
    /// </summary>
    public class DiagnosticsUIService : IDiagnosticsUIService
    {
        private readonly RagDiagnosticsService _diagnostics;
        private Window? _ownerWindow;
        private RagDiagnosticWindow? _diagnosticWindow;
        private ShowDiagnosticsCommand? _showDiagnosticsCommand;

        /// <summary>
        /// Initializes a new instance of the DiagnosticsUIService
        /// </summary>
        public DiagnosticsUIService(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _showDiagnosticsCommand = new ShowDiagnosticsCommand(diagnostics);

            _diagnostics.Log(DiagnosticLevel.Info, "DiagnosticsUIService", "Initialized DiagnosticsUIService");
        }

        /// <summary>
        /// Sets up diagnostics UI elements in the given window
        /// </summary>
        public void SetupDiagnosticsUI(Window window)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            _ownerWindow = window;

            try
            {
                // Register keyboard shortcut handler
                RegisterShortcutHandler(() => ShowDiagnosticsWindow());

                _diagnostics.Log(DiagnosticLevel.Info, "DiagnosticsUIService",
                    "Diagnostics UI setup complete for window: " + window.GetType().Name);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DiagnosticsUIService",
                    $"Error setting up diagnostics UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers a handler for diagnostic shortcut keys
        /// </summary>
        public void RegisterShortcutHandler(Action showDiagnosticsAction)
        {
            if (showDiagnosticsAction == null)
                throw new ArgumentNullException(nameof(showDiagnosticsAction));

            if (_ownerWindow == null)
            {
                _diagnostics.Log(DiagnosticLevel.Warning, "DiagnosticsUIService",
                    "Cannot register shortcut handler: no owner window set");
                return;
            }

            // Add keyboard event handler
            _ownerWindow.KeyDown += (sender, e) =>
            {
                // Ctrl+Shift+D shortcut for diagnostics window
                if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    showDiagnosticsAction();
                    e.Handled = true;
                }
            };

            _diagnostics.Log(DiagnosticLevel.Debug, "DiagnosticsUIService",
                "Registered keyboard shortcut handler");
        }

        /// <summary>
        /// Shows the diagnostics window
        /// </summary>
        public void ShowDiagnosticsWindow()
        {
            try
            {
                if (_diagnosticWindow == null || !_diagnosticWindow.IsVisible)
                {
                    // Get the diagnostic window from DI
                    _diagnosticWindow = ServiceProviderFactory.GetService<RagDiagnosticWindow>();

                    if (_diagnosticWindow != null)
                    {
                        if (_ownerWindow != null)
                        {
                            _diagnosticWindow.Owner = _ownerWindow;
                        }

                        _diagnosticWindow.Show();
                        _diagnostics.Log(DiagnosticLevel.Info, "DiagnosticsUIService",
                            "Diagnostics window opened");
                    }
                    else
                    {
                        _diagnostics.Log(DiagnosticLevel.Error, "DiagnosticsUIService",
                            "Failed to get diagnostics window from service provider");
                    }
                }
                else
                {
                    _diagnosticWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "DiagnosticsUIService",
                    $"Error opening diagnostics window: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a command for showing the diagnostics window
        /// </summary>
        public ICommand GetShowDiagnosticsCommand()
        {
            return _showDiagnosticsCommand ??
                   (_showDiagnosticsCommand = new ShowDiagnosticsCommand(_diagnostics));
        }

        /// <summary>
        /// Unregisters all event handlers
        /// </summary>
        public void UnregisterHandlers()
        {
            // In a complete implementation, we would store references to event handlers
            // and unregister them explicitly. For this implementation, we'll rely on the
            // window's Closed event handling.

            _diagnosticWindow?.Close();
            _diagnosticWindow = null;

            _diagnostics.Log(DiagnosticLevel.Info, "DiagnosticsUIService",
                "Diagnostics handlers unregistered");
        }
    }
}