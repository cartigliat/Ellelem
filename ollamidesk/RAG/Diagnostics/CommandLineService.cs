using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ollamidesk.Services
{
	/// <summary>
	/// Service for executing command line operations
	/// </summary>
	public class CommandLineService
	{
		/// <summary>
		/// Executes a command asynchronously and returns the output
		/// </summary>
		/// <param name="fileName">The command or executable to run</param>
		/// <param name="arguments">The arguments to pass to the command</param>
		/// <returns>A tuple containing the standard output and error output</returns>
		public async Task<(string output, string error)> ExecuteCommandAsync(string fileName, string arguments)
		{
			using var process = new Process();

			process.StartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			var outputBuilder = new System.Text.StringBuilder();
			var errorBuilder = new System.Text.StringBuilder();

			// Set up output handling
			process.OutputDataReceived += (sender, args) =>
			{
				if (args.Data != null)
				{
					outputBuilder.AppendLine(args.Data);
				}
			};

			process.ErrorDataReceived += (sender, args) =>
			{
				if (args.Data != null)
				{
					errorBuilder.AppendLine(args.Data);
				}
			};

			try
			{
				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				await process.WaitForExitAsync();

				return (outputBuilder.ToString(), errorBuilder.ToString());
			}
			catch (Exception ex)
			{
				return (string.Empty, $"Error executing command: {ex.Message}");
			}
		}
	}
}