using Time.Tracking.Jira;
using Time.Tracking.Jira.Wpf.Infrastructure;
using Time.Tracking.Jira.Wpf.ViewModels;
using System;
using System.Collections.Generic;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// Records what the ledger asked the user, and answers with whatever the test set up.
    /// </summary>
    internal sealed class FakeDialogService : IDialogService
    {
        public List<string> Infos = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();
        public List<string> Questions = new List<string>();

        /// <summary>What <see cref="Confirm"/> answers. Default is "no".</summary>
        public bool ConfirmAnswer { get; set; }

        public int WorklogShown { get; private set; }
        public WorklogChoice WorklogAnswer { get; set; }

        public int PickIssueShown { get; private set; }
        public IssueSelection PickIssueAnswer { get; set; }

        public int EditTimeShown { get; private set; }
        public TimeSpan? EditTimeAnswer { get; set; }

        public int EditSettingsShown { get; private set; }
        public SettingsChoice EditSettingsAnswer { get; set; }

        public int LogAllShown { get; private set; }
        public LogAllViewModel LastLogAll { get; private set; }

        public void Info(string message, string title) { Infos.Add(message); }
        public void Warn(string message, string title) { Warnings.Add(message); }
        public void Error(string message, string title) { Errors.Add(message); }

        public bool Confirm(string message, string title)
        {
            Questions.Add(message);
            return ConfirmAnswer;
        }

        public IssueSelection PickIssue(LedgerViewModel ledger, TimeEntryViewModel entry)
        {
            PickIssueShown++;
            return PickIssueAnswer;
        }

        public TimeSpan? EditTime(string issueKey, string issueSummary, TimeSpan elapsed)
        {
            EditTimeShown++;
            return EditTimeAnswer;
        }

        public WorklogChoice Worklog(WorklogViewModel vm)
        {
            WorklogShown++;
            return WorklogAnswer;
        }

        public void LogAll(LogAllViewModel vm)
        {
            LogAllShown++;
            LastLogAll = vm;
        }

        public SettingsChoice EditSettings(Settings settings)
        {
            EditSettingsShown++;
            return EditSettingsAnswer
                ?? new SettingsChoice { Accepted = false, ImportedRows = new List<GridPersistedRow>() };
        }
    }
}
