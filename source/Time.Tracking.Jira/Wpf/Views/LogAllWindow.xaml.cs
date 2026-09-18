using Time.Tracking.Jira.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Time.Tracking.Jira.Wpf.Views
{
    /// <summary>
    /// Log all: every row with time in one list, each with its own comment, sent together.
    /// Stays open after sending, so each row's outcome can be read — and a failed one fixed
    /// and sent again — before closing.
    /// </summary>
    /// <remarks>
    /// Closed with <see cref="Window.Close"/> rather than a DialogResult: there is no answer to
    /// give back, the rows report their own outcome. And a DialogResult set while sending —
    /// when closing is refused — would stay set and turn the next Close into a no-op.
    /// </remarks>
    public partial class LogAllWindow : Window
    {
        private readonly LogAllViewModel vm;

        public LogAllWindow(LogAllViewModel vm)
        {
            InitializeComponent();

            this.vm = vm;
            DataContext = vm;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Rows are still on their way to Jira: closing now would hide how they end
            if (vm.IsPosting)
                e.Cancel = true;

            base.OnClosing(e);
        }
    }
}
