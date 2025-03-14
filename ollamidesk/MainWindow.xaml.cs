using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ollamidesk
{
    public partial class MainWindow : Window
    {
        private Dictionary<string, ObservableCollection<ChatMessage>> chatHistories =
            new Dictionary<string, ObservableCollection<ChatMessage>>();
        private string? loadedDocument;
        private IOllamaModel? selectedModel;

        public MainWindow()
        {
            InitializeComponent(); // This should now be recognized
        }

        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SideMenuWindow sideMenuWindow = new SideMenuWindow();

            if (sideMenuWindow.ShowDialog() == true)
            {
                // Update the selected model in the main window
                if (!string.IsNullOrEmpty(sideMenuWindow.SelectedModel))
                {
                    ModelNameTextBlock.Text = sideMenuWindow.SelectedModel;

                    // Initialize chat history for this model if not exists
                    if (!chatHistories.ContainsKey(sideMenuWindow.SelectedModel))
                    {
                        chatHistories[sideMenuWindow.SelectedModel] = new ObservableCollection<ChatMessage>();
                    }

                    // Set the ItemsSource to the current model's chat history
                    ChatHistoryItemsControl.ItemsSource = chatHistories[sideMenuWindow.SelectedModel];

                    // Load the selected model
                    selectedModel = OllamaModelLoader.LoadModel(sideMenuWindow.SelectedModel);
                }

                // Update the loaded document
                if (!string.IsNullOrEmpty(sideMenuWindow.LoadedDocument))
                {
                    loadedDocument = sideMenuWindow.LoadedDocument;
                }
            }
        }

        private async void SendMessage()
        {
            if (selectedModel == null)
            {
                MessageBox.Show("Please select a model first.", "No Model Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string userInput = string.Empty;
            await Dispatcher.InvokeAsync(() =>
            {
                userInput = UserInputTextBox.Text.Trim();
            });

            if (string.IsNullOrEmpty(userInput))
            {
                return;
            }

            string selectedModelName = string.Empty;
            await Dispatcher.InvokeAsync(() =>
            {
                selectedModelName = ModelNameTextBlock.Text;

                // Disable input and show loading indicator
                UserInputTextBox.IsEnabled = false;
                SendButton.IsEnabled = false;
                LoadingIndicator.Visibility = Visibility.Visible;
            });

            try
            {
                // Initialize chat history for this model if not exists
                if (!chatHistories.ContainsKey(selectedModelName))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        chatHistories[selectedModelName] = new ObservableCollection<ChatMessage>();
                    });
                }

                // Get current chat history for context
                var currentChatHistory = await Dispatcher.InvokeAsync(() =>
                {
                    return chatHistories[selectedModelName]
                        .Select(cm => $"User: {cm.UserQuery}\nModel: {cm.ModelResponse}")
                        .ToList();
                });

                // Generate model response
                string modelResponse = await selectedModel.GenerateResponseAsync(
                    userInput,
                    loadedDocument ?? string.Empty,
                    currentChatHistory
                );

                // Create and add chat message
                var chatMessage = new ChatMessage
                {
                    UserQuery = userInput,
                    ModelResponse = modelResponse
                };

                // Add the chat message to the appropriate model's chat history
                await Dispatcher.InvokeAsync(() =>
                {
                    // Manage chat history size (keep last 50 messages)
                    var history = chatHistories[selectedModelName];
                    if (history.Count >= 50)
                    {
                        history.RemoveAt(0);
                    }
                    history.Add(chatMessage);
                });

                // Clear input and scroll to bottom
                await Dispatcher.InvokeAsync(() =>
                {
                    UserInputTextBox.Clear();
                    ChatHistoryScrollViewer.ScrollToBottom();
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                // Re-enable input and hide loading indicator
                await Dispatcher.InvokeAsync(() =>
                {
                    UserInputTextBox.IsEnabled = true;
                    SendButton.IsEnabled = true;
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                    UserInputTextBox.Focus();
                });
            }
        }

        private async void UserInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Allow sending message with Shift+Enter (new line) or Ctrl+Enter
            if (e.Key == Key.Enter)
            {
                // Check if Shift or Ctrl is pressed
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ||
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Insert new line
                    int caretIndex = UserInputTextBox.SelectionStart;
                    UserInputTextBox.Text = UserInputTextBox.Text.Insert(caretIndex, Environment.NewLine);
                    UserInputTextBox.SelectionStart = caretIndex + Environment.NewLine.Length;
                }
                else
                {
                    // Send message
                    await Task.Run(SendMessage);
                    e.Handled = true;
                }
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(SendMessage);
        }
    }

    // Persist the ChatMessage class definition
    public class ChatMessage
    {
        public string? UserQuery { get; set; }
        public string? ModelResponse { get; set; }
    }
}