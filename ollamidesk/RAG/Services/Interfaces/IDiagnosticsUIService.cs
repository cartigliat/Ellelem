using System;
using System.Windows;
using System.Windows.Input;

namespace ollamidesk.RAG.Services.Interfaces
{
    /// <summary>
    /// Service for UI-related diagnostics functionality
    /// </summary>
    public interface IDiagnosticsUIService
    {
        /// <summary>
        /// Sets up diagnostics UI elements in the given window
        /// </summary>
        void SetupDiagnosticsUI(Window window);

        /// <summary>
        /// Registers a handler for diagnostic shortcut keys
        /// </summary>
        void RegisterShortcutHandler(Action showDiagnosticsAction);

        /// <summary>
        /// Shows the diagnostics window
        /// </summary>
        void ShowDiagnosticsWindow();

        /// <summary>
        /// Unregisters all event handlers
        /// </summary>
        void UnregisterHandlers();

        /// <summary>
        /// Gets a command for showing the diagnostics window
        /// </summary>
        ICommand GetShowDiagnosticsCommand();
    }
}