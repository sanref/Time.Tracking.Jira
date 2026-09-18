using System;
using System.Windows;
using System.Windows.Controls;

namespace Time.Tracking.Jira.Wpf.Views
{
    /// <summary>
    /// Sets a row's clock by hand. The value is Jira time ("2h 15m", "2.25h"), parsed with the
    /// same helper the worklog posts go through, so what this dialog accepts and what Jira
    /// accepts cannot drift apart.
    /// </summary>
    public partial class EditTimeWindow : Window
    {
        public EditTimeWindow(string issueKey, string issueSummary, TimeSpan time)
        {
            InitializeComponent();

            Time = time;

            // A row can be timed before it has an issue: say so rather than showing a gap
            IssueKeyText.Text = string.IsNullOrEmpty(issueKey) ? "Row with no issue yet" : issueKey;
            IssueSummaryText.Text = issueSummary ?? "";
            CurrentTimeText.Text = JiraTimeHelpers.TimeSpanToJiraTime(time);

            TimeBox.Text = JiraTimeHelpers.TimeSpanToJiraTime(time);

            // Opens selected: the point of the dialog is replacing the value, not appending
            Loaded += delegate
            {
                TimeBox.Focus();
                TimeBox.SelectAll();
            };
        }

        /// <summary>The time that was typed. Only meaningful once the dialog was accepted.</summary>
        public TimeSpan Time { get; private set; }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TimeSpan? parsed = JiraTimeHelpers.JiraTimeToTimeSpan(TimeBox.Text);
            if (parsed == null)
            {
                Hint.Visibility = Visibility.Collapsed;
                Error.Visibility = Visibility.Visible;

                TimeBox.Focus();
                TimeBox.SelectAll();
                return;
            }

            Time = parsed.Value;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>Typing again drops the complaint, the way the v3 dialog cleared its red field.</summary>
        private void Time_TextChanged(object sender, TextChangedEventArgs e)
        {
            Error.Visibility = Visibility.Collapsed;
            Hint.Visibility = Visibility.Visible;
        }
    }
}
