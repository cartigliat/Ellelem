using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ollamidesk
{
    public static class CommandLineUtil
    {
        public static async Task<(string output, string error)> ExecuteCommandAsync(string command, string arguments)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process())
            {
                process.StartInfo = processInfo;
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();
                return (output, error);
            }
        }

        public static string CleanModelOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
                return string.Empty;

            // First pass: Remove standard ANSI escape sequences
            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                output,
                @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])",
                string.Empty
            );

            // Second pass: Remove more complex ANSI sequences (including those in your error)
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\[\d+[mhJKG]|\[\?\d+[hl]|\[\d+;\d+[Hf]",
                string.Empty
            );

            // Third pass: Remove any remaining control characters (ASCII 0-31 except newline/tab)
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"[\x00-\x08\x0B-\x1F\x7F]",
                string.Empty
            );

            // Fourth pass: If all else fails, only keep printable characters
            cleaned = new string(cleaned.Where(c =>
                (c >= ' ' && c <= '~') || c == '\n' || c == '\r' || c == '\t').ToArray());

            return cleaned.Trim();
        }
    }
}