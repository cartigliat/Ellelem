using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ollamidesk.Configuration
{
    /// <summary>
    /// Provides access to application configuration
    /// </summary>
    public class ConfigurationProvider
    {
        private readonly string _configFilePath;
        private AppSettings _settings;

        /// <summary>
        /// Initializes a new instance of the ConfigurationProvider class
        /// </summary>
        /// <param name="configFilePath">Path to the configuration file</param>
        public ConfigurationProvider(string? configFilePath = null)
        {
            _configFilePath = configFilePath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            // Initialize with default settings
            _settings = new AppSettings();
        }

        /// <summary>
        /// Gets the current application settings
        /// </summary>
        public AppSettings Settings => _settings;

        /// <summary>
        /// Loads configuration from the configuration file
        /// </summary>
        /// <returns>The loaded application settings</returns>
        public AppSettings LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

                    if (loadedSettings != null)
                    {
                        _settings = loadedSettings;
                    }

                    // Ensure directories exist
                    EnsureDirectoriesExist();
                }
                else
                {
                    // Create default configuration file
                    SaveConfiguration();
                }
            }
            catch (Exception ex)
            {
                // Log the error but continue with default settings
                Console.WriteLine($"Error loading configuration: {ex.Message}");
            }

            return _settings;
        }

        /// <summary>
        /// Saves the current configuration to the configuration file
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                // Create directory if it doesn't exist
                string? directoryPath = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                // Serialize settings with indented formatting
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);

                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures that all necessary directories exist
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            try
            {
                Directory.CreateDirectory(_settings.Storage.BasePath);
                Directory.CreateDirectory(_settings.Storage.DocumentsFolder);
                Directory.CreateDirectory(_settings.Storage.VectorsFolder);
                Directory.CreateDirectory(_settings.Storage.EmbeddingsFolder);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating directories: {ex.Message}");
            }
        }
    }
}