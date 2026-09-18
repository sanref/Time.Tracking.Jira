using Time.Tracking.Jira.Wpf.Infrastructure;
using Time.Tracking.Jira.Wpf.ViewModels;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Time.Tracking.Jira.Wpf.Views
{
    /// <summary>
    /// Picks the parent issue and, optionally, the subtask for a ledger row. Same two JQL
    /// queries v3 used to fill its combo box columns.
    /// </summary>
    public partial class AddIssueDialog : Window
    {
        private readonly LedgerViewModel owner;

        // Parent whose subtasks are currently in CboSubtask, so re-entering the field does
        // not re-query Jira for the same key.
        private string loadedSubtasksFor;

        // The row's own subtask, captured when the dialog opens in edit mode. Jira's subtask
        // JQL can legitimately omit it from a later fetch (e.g. it's Done and the query only
        // returns open subtasks) — this keeps it selectable instead of it silently falling out
        // of the row on Save. Cleared as soon as the user picks or types a different parent,
        // since it no longer applies once that happens.
        private IssueItem originalSubtask;

        internal AddIssueDialog(LedgerViewModel owner, TimeEntryViewModel entry)
        {
            this.owner = owner;

            InitializeComponent();

            // The live collection, not a copy: an issue the search finds in Jira joins it
            // while the dialog is open, and has to show up in the list.
            CboParent.ItemsSource = owner.ParentIssues;
            IssueSearch.SetLookup(CboParent, owner.IssueLookup);

            if (entry == null)
                return;

            Title = "Edit issue";
            Kicker.Text = "EDIT ROW";
            BtnAccept.Content = "Save";

            CboParent.Text = entry.ParentKey;
            ParentKey = entry.ParentKey;
            ParentSummary = entry.ParentSummary;

            if (!string.IsNullOrEmpty(entry.SubtaskKey))
            {
                originalSubtask = new IssueItem(entry.SubtaskKey, entry.SubtaskSummary);
                CboSubtask.ItemsSource = new List<IssueItem> { originalSubtask };
                CboSubtask.SelectedIndex = 0;
            }

            LoadSubtasks(entry.ParentKey, entry.SubtaskKey);
        }

        public string ParentKey { get; private set; }

        public string ParentSummary { get; private set; }

        public string SubtaskKey { get; private set; }

        public string SubtaskSummary { get; private set; }

        private void Parent_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            IssueItem selected = CboParent.SelectedItem as IssueItem;
            if (selected == null)
                return;

            LoadSubtasks(selected.Key, null);
        }

        private void Parent_LostFocus(object sender, RoutedEventArgs e)
        {
            // Covers a key typed by hand, which never raises SelectionChanged
            LoadSubtasks(CurrentText(CboParent), null);
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            string parentKey = CurrentText(CboParent);
            if (string.IsNullOrEmpty(parentKey))
            {
                MessageBox.Show("Please select or type a parent issue", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CboParent.Focus();
                return;
            }

            ParentKey = parentKey;
            ParentSummary = SummaryFor(CboParent, parentKey);

            SubtaskKey = CurrentText(CboSubtask);
            SubtaskSummary = string.IsNullOrEmpty(SubtaskKey) ? "" : SummaryFor(CboSubtask, SubtaskKey);

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void LoadSubtasks(string parentKey, string keepSelectedKey)
        {
            if (string.IsNullOrEmpty(parentKey) || parentKey == loadedSubtasksFor)
                return;

            // Only the initial edit-mode load (which passes the row's own subtask as
            // keepSelectedKey) may fall back to originalSubtask in ShowSubtasks. Once the
            // parent actually changes, that subtask belongs to a different issue and must not
            // resurface in this one's list.
            if (keepSelectedKey == null)
                originalSubtask = null;

            if (!owner.Jira.SessionValid)
            {
                SubtaskStatus.Text = "Not connected to Jira — type the subtask key by hand, or leave it empty.";
                return;
            }

            loadedSubtasksFor = parentKey;
            SubtaskStatus.Text = "Loading subtasks…";

            owner.FetchSubtasks(parentKey, (subtasks, error) => ShowSubtasks(parentKey, subtasks, error, keepSelectedKey));
        }

        private void ShowSubtasks(string parentKey, List<IssueItem> subtasks, string error, string keepSelectedKey)
        {
            if (parentKey != loadedSubtasksFor)
                return;

            if (error != null)
            {
                SubtaskStatus.Text = "Could not load subtasks. Type the key by hand, or leave it empty.";
                loadedSubtasksFor = null;
                return;
            }

            string previous = keepSelectedKey ?? CurrentText(CboSubtask);

            // The row's original subtask fell out of this fetch — keep it in the list instead
            // of losing the row's subtask assignment silently on Save.
            if (originalSubtask != null && originalSubtask.Key == previous && !subtasks.Exists(x => x.Key == previous))
                subtasks = new List<IssueItem>(subtasks) { originalSubtask };

            CboSubtask.ItemsSource = subtasks;
            CboSubtask.SelectedItem = subtasks.Find(x => x.Key == previous);

            if (CboSubtask.SelectedItem == null)
                CboSubtask.Text = "";

            SubtaskStatus.Text = subtasks.Count == 0
                ? string.Format("{0} has no subtasks. The time will be logged against it.", parentKey)
                : "Leave empty to log the time against the parent issue.";
        }

        private static string CurrentText(ComboBox combo)
        {
            IssueItem selected = combo.SelectedItem as IssueItem;
            if (selected != null)
                return selected.Key;

            return (combo.Text ?? "").Trim();
        }

        /// <summary>The title that goes with <paramref name="key"/>, empty if it was typed by hand.</summary>
        private static string SummaryFor(ComboBox combo, string key)
        {
            IEnumerable items = combo.ItemsSource as IEnumerable;
            if (items == null)
                return "";

            foreach (object item in items)
            {
                IssueItem issue = item as IssueItem;
                if (issue != null && issue.Key == key)
                    return issue.Summary;
            }

            return "";
        }
    }
}
