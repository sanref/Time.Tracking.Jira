using Time.Tracking.Jira.Wpf.Infrastructure;
using Time.Tracking.Jira.Wpf.ViewModels;
using System.Windows;

namespace Time.Tracking.Jira.Wpf.Views
{
    /// <summary>
    /// Submits a worklog, or parks it. <see cref="DialogResult"/> true means Submit;
    /// false covers both Cancel and "Save for later" — <see cref="SavedForLater"/> tells
    /// them apart, same two-state contract the WinForms WorklogForm had with its DialogResult
    /// of OK vs. Yes vs. Cancel.
    /// </summary>
    public partial class WorklogWindow : Window
    {
        public bool SavedForLater { get; private set; }

        private readonly WorklogViewModel vm;

        public WorklogWindow(WorklogViewModel vm)
        {
            InitializeComponent();

            this.vm = vm;
            vm.SubmitCommand = new RelayCommand(Submit);
            DataContext = vm;
        }

        private void Submit()
        {
            if (vm.StartedAt == null)
            {
                MessageBox.Show(this, "Start time is not a valid time (use HH:mm).",
                    "Submit worklog", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!vm.IsEstimateValueValid)
            {
                MessageBox.Show(this, "That is not a valid Jira time (examples: 1h, 30m, 2d).",
                    "Submit worklog", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            Submit();
        }

        private void SaveForLater_Click(object sender, RoutedEventArgs e)
        {
            SavedForLater = true;
            DialogResult = false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
