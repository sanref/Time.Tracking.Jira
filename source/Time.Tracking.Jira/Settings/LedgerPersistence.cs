using Time.Tracking.Jira.Wpf.ViewModels;
using System;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Maps a ledger row to and from the <see cref="GridPersistedRow"/> that settings.json
    /// stores. The rules that decide what survives a restart live here, away from the
    /// collections and the dispatcher, so they can be exercised on their own.
    /// </summary>
    internal static class LedgerPersistence
    {
        /// <summary>
        /// Rebuilds a row from what was saved. <paramref name="onExit"/> decides what happens
        /// to the clock; <paramref name="lookupParentSummary"/> supplies the parent title,
        /// which is never persisted and has to be resolved from Jira later.
        /// </summary>
        public static TimeEntryViewModel ToEntry(GridPersistedRow row, SaveTimerSetting onExit,
            Func<string, string> lookupParentSummary)
        {
            string subtaskKey, subtaskSummary;
            SplitDisplayValue(row.Subtask, out subtaskKey, out subtaskSummary);

            string parentKey = (row.ParentIssue ?? "").Trim();
            string parentSummary = lookupParentSummary != null ? (lookupParentSummary(parentKey) ?? "") : "";

            TimeEntryViewModel entry = new TimeEntryViewModel(parentKey, parentSummary, subtaskKey, subtaskSummary);
            entry.IsPinned = row.Pinned;

            // Not gated on having an issue: a row can accumulate time untagged and must come
            // back with it after a restart.
            if (row.TotalTime.TotalSeconds > 0)
            {
                if (onExit != SaveTimerSetting.NoSave)
                {
                    entry.Timer.SetState(new TimerState
                    {
                        Running = onExit == SaveTimerSetting.SaveRunActive && row.TimerRunning,
                        SessionStartTime = row.SessionStartTime,
                        InitialStartTime = row.InitialStartTime,
                        TotalTime = row.TotalTime
                    });
                }
                else
                {
                    entry.Timer.TimeElapsed = row.TotalTime;
                }
            }

            return entry;
        }

        /// <summary>
        /// The row to store, or null when there is nothing worth storing. A row with neither
        /// issue nor time is one added inline and abandoned: saving it would bring it back
        /// forever. That is the same condition under which it is removed without a question.
        /// </summary>
        public static GridPersistedRow ToRow(TimeEntryViewModel entry)
        {
            if (entry.IsEmpty)
                return null;

            TimerState state = entry.Timer.GetState();

            return new GridPersistedRow
            {
                ParentIssue = entry.ParentKey,
                Subtask = JoinDisplayValue(entry.SubtaskKey, entry.SubtaskSummary),
                TimerRunning = state.Running,
                SessionStartTime = state.SessionStartTime,
                InitialStartTime = state.InitialStartTime,
                TotalTime = state.TotalTime,
                Pinned = entry.IsPinned
            };
        }

        /// <summary>
        /// v3 stored the subtask as "KEY - Summary" in a single cell. Both halves of the
        /// split stay compatible with it, so a v3 configuration still loads.
        /// </summary>
        public static void SplitDisplayValue(string value, out string key, out string summary)
        {
            key = (value ?? "").Trim();
            summary = "";

            if (key.Length == 0)
                return;

            int separator = key.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
                return;

            summary = key.Substring(separator + 3).Trim();
            key = key.Substring(0, separator).Trim();
        }

        public static string JoinDisplayValue(string key, string summary)
        {
            if (string.IsNullOrEmpty(key))
                return "";

            return string.IsNullOrEmpty(summary) ? key : key + " - " + summary;
        }
    }
}
