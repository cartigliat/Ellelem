using System.Windows;

// Assuming the Dialogs folder is directly under ollamidesk project root for namespace
namespace ollamidesk.Dialogs
{
    public partial class CustomConfirmDialog : Window
    {
        public static readonly DependencyProperty DialogTitleProperty =
            DependencyProperty.Register("DialogTitle", typeof(string), typeof(CustomConfirmDialog), new PropertyMetadata("Confirm Action"));

        public string DialogTitle
        {
            get { return (string)GetValue(DialogTitleProperty); }
            set { SetValue(DialogTitleProperty, value); }
        }

        public static readonly DependencyProperty DialogMessageProperty =
            DependencyProperty.Register("DialogMessage", typeof(string), typeof(CustomConfirmDialog), new PropertyMetadata("Are you sure?"));

        public string DialogMessage
        {
            get { return (string)GetValue(DialogMessageProperty); }
            set { SetValue(DialogMessageProperty, value); }
        }

        // Default constructor for XAML previewer and other cases
        public CustomConfirmDialog()
        {
            InitializeComponent();
        }

        public CustomConfirmDialog(string title, string message)
        {
            InitializeComponent();
            this.DialogTitle = title;
            this.DialogMessage = message;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
