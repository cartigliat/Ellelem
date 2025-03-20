using System;
using System.Windows.Input;
using ollamidesk.DependencyInjection;
using ollamidesk.RAG.Diagnostics;

namespace ollamidesk.RAG.Services.Implementations
{
    /// <summary>
    /// Command for showing the diagnostics window
    /// </summary>
    public class ShowDiagnosticsCommand : ICommand
    {
        private readonly RagDiagnosticsService _diagnostics;

        public event EventHandler? CanExecuteChanged;

        public ShowDiagnosticsCommand(RagDiagnosticsService diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            try
            {
                var window = ServiceProviderFactory.GetService<RagDiagnosticWindow>();
                window?.Show();

                _diagnostics.Log(DiagnosticLevel.Info, "ShowDiagnosticsCommand", "Diagnostics window opened");
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Error, "ShowDiagnosticsCommand", $"Error opening diagnostics window: {ex.Message}");
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}