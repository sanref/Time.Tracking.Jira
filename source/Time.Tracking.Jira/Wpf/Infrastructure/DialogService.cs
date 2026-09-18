using Time.Tracking.Jira.Logging;
using Time.Tracking.Jira.Wpf.ViewModels;
using Time.Tracking.Jira.Wpf.Views;
using System;
using System.Collections.Generic;
using System.Windows;

namespace Time.Tracking.Jira.Wpf.Infrastructure
{
    /// <summary>
    /// The real dialogs, owned by the tracker window.
    /// </summary>
    internal sealed class DialogService : IDialogService
    {
        /// <summary>Owner for the modal dialogs. Set by the window once it has a handle.</summary>
        internal Window Owner { get; set; }

        public void Info(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void Warn(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void Error(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Confirm(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes;
        }

        public IssueSelection PickIssue(LedgerViewModel ledger, TimeEntryViewModel entry)
        {
            AddIssueDialog dialog;
            if (ShowModal(() => new AddIssueDialog(ledger, entry), out dialog) != true)
                return null;

            return new IssueSelection
            {
                ParentKey = dialog.ParentKey,
                ParentSummary = dialog.ParentSummary,
                SubtaskKey = dialog.SubtaskKey,
                SubtaskSummary = dialog.SubtaskSummary
            };
        }

        public TimeSpan? EditTime(string issueKey, string issueSummary, TimeSpan elapsed)
        {
            EditTimeWindow dialog;
            if (ShowModal(() => new EditTimeWindow(issueKey, issueSummary, elapsed), out dialog) != true)
                return null;

            return dialog.Time;
        }

        public WorklogChoice Worklog(WorklogViewModel vm)
        {
            WorklogWindow dialog;
            bool? outcome = ShowModal(() => new WorklogWindow(vm), out dialog);

            if (outcome == true)
                return WorklogChoice.Post;
            if (outcome == null)
                return WorklogChoice.CouldNotOpen;

            return dialog.SavedForLater ? WorklogChoice.SaveForLater : WorklogChoice.Cancel;
        }

        public void LogAll(LogAllViewModel vm)
        {
            LogAllWindow dialog;
            ShowModal(() => new LogAllWindow(vm), out dialog);
        }

        public SettingsChoice EditSettings(Settings settings)
        {
            SettingsWindow dialog;
            if (ShowModal(() => new SettingsWindow(settings), out dialog) != true)
                return new SettingsChoice { Accepted = false, ImportedRows = new List<GridPersistedRow>() };

            return new SettingsChoice { Accepted = true, ImportedRows = dialog.ImportedRows };
        }

        /// <summary>
        /// Builds a modal dialog, owns it to the tracker window and shows it. Returns what
        /// ShowDialog returned, or null if the dialog could not be opened at all.
        /// </summary>
        /// <remarks>
        /// Constructing a window runs the XAML parser, and anything a control or an attached
        /// property throws in there travels straight up to Dispatcher.UnhandledException and
        /// closes the application — a client lost a session that way in 4.1.2, on the worklog
        /// dialog. Losing one dialog is recoverable; losing the running timers is not.
        /// </remarks>
        private bool? ShowModal<T>(Func<T> build, out T dialog) where T : Window
        {
            dialog = null;
            try
            {
                dialog = build();
                dialog.Owner = Owner;
                return dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Could not open the {0} dialog.", typeof(T).Name), ex);
                Error(
                    string.Format("Could not open this window.{0}{0}{1}{0}{0}The details are in the log; your timers are untouched.",
                        Environment.NewLine, ex.Message),
                    "Error");
                return null;
            }
        }
    }
}
