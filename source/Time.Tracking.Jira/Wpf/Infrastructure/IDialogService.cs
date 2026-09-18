using Time.Tracking.Jira.Wpf.ViewModels;
using System;
using System.Collections.Generic;

namespace Time.Tracking.Jira.Wpf.Infrastructure
{
    /// <summary>What an issue picker came back with. Null stands for "cancelled".</summary>
    internal sealed class IssueSelection
    {
        public string ParentKey { get; set; }
        public string ParentSummary { get; set; }
        public string SubtaskKey { get; set; }
        public string SubtaskSummary { get; set; }
    }

    /// <summary>
    /// How the worklog dialog ended. <see cref="CouldNotOpen"/> is deliberately distinct from
    /// <see cref="Cancel"/>: the caller parks the comment on a cancel, and there is nothing to
    /// park when the window never appeared.
    /// </summary>
    internal enum WorklogChoice
    {
        Post,
        SaveForLater,
        Cancel,
        CouldNotOpen
    }

    /// <summary>What the settings dialog came back with.</summary>
    internal sealed class SettingsChoice
    {
        public bool Accepted { get; set; }
        public List<GridPersistedRow> ImportedRows { get; set; }
    }

    /// <summary>
    /// Every way the ledger talks to the user: message boxes and modal dialogs.
    /// </summary>
    /// <remarks>
    /// This exists so the view model can be exercised without a message pump. Before it, a
    /// LedgerViewModel held a Window and called MessageBox.Show directly, which put every
    /// path that warns or asks — most of them — out of reach of a test.
    /// </remarks>
    internal interface IDialogService
    {
        void Info(string message, string title);
        void Warn(string message, string title);
        void Error(string message, string title);

        /// <summary>A yes/no question. False for anything that is not an explicit yes.</summary>
        bool Confirm(string message, string title);

        /// <summary>
        /// The Add issue dialog, also used to change the issue on an existing row.
        /// <paramref name="entry"/> is null when adding.
        /// </summary>
        IssueSelection PickIssue(LedgerViewModel ledger, TimeEntryViewModel entry);

        /// <summary>The elapsed time set by hand, or null if the dialog was not accepted.</summary>
        TimeSpan? EditTime(string issueKey, string issueSummary, TimeSpan elapsed);

        /// <summary>Shows the worklog dialog. <paramref name="vm"/> carries the edits back.</summary>
        WorklogChoice Worklog(WorklogViewModel vm);

        /// <summary>
        /// Shows Log all until the user closes it. The sending happens while it is open, through
        /// <paramref name="vm"/>; there is nothing to return.
        /// </summary>
        void LogAll(LogAllViewModel vm);

        SettingsChoice EditSettings(Settings settings);
    }
}
