using Time.Tracking.Jira.Wpf.Infrastructure;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Time.Tracking.Jira.Wpf.ViewModels
{
    /// <summary>
    /// One subtask row. It owns a <see cref="WatchTimer"/> rather than a plain second
    /// counter: the worklog POST needs <see cref="WatchTimer.GetInitialStartTime"/>, and
    /// <see cref="TimerState"/> is what gets persisted between runs.
    /// </summary>
    public class TimeEntryViewModel : ObservableObject
    {
        private readonly WatchTimer timer = new WatchTimer();

        private string parentKey;
        private string parentSummary;
        private string subtaskKey;
        private string subtaskSummary;
        private int index;
        private bool isEditing;
        private bool isPinned;
        private string comment = "";
        private EstimateUpdateMethods estimateUpdateMethod = EstimateUpdateMethods.Auto;
        private string estimateUpdateValue = "";

        public TimeEntryViewModel(string parentKey, string parentSummary, string subtaskKey, string subtaskSummary)
        {
            this.parentKey = parentKey ?? "";
            this.parentSummary = parentSummary ?? "";
            this.subtaskKey = subtaskKey ?? "";
            this.subtaskSummary = subtaskSummary ?? "";
        }

        internal WatchTimer Timer
        {
            get { return timer; }
        }

        public string ParentKey
        {
            get { return parentKey; }
            set { if (Set(ref parentKey, value ?? "")) RaiseIdentityChanged(); }
        }

        public string ParentSummary
        {
            get { return parentSummary; }
            set { Set(ref parentSummary, value ?? ""); }
        }

        public string SubtaskKey
        {
            get { return subtaskKey; }
            set { if (Set(ref subtaskKey, value ?? "")) RaiseIdentityChanged(); }
        }

        public string SubtaskSummary
        {
            get { return subtaskSummary; }
            set { Set(ref subtaskSummary, value ?? ""); }
        }

        /// <summary>
        /// The issue the time is logged against: the subtask when there is one, otherwise the
        /// parent. Same rule as v3's GetTimerKeyFromRow, so persisted rows keep their identity.
        /// </summary>
        public string TimerKey
        {
            get { return string.IsNullOrEmpty(subtaskKey) ? parentKey : subtaskKey; }
        }

        public bool HasIssue
        {
            get { return !string.IsNullOrEmpty(TimerKey); }
        }

        /// <summary>The title that goes with <see cref="TimerKey"/> — same subtask-else-parent rule.</summary>
        public string TimerSummary
        {
            get { return string.IsNullOrEmpty(subtaskKey) ? parentSummary : subtaskSummary; }
        }

        /// <summary>
        /// Draft state for the worklog dialog: what you typed and which estimate option you
        /// picked, kept so "Save for later" has somewhere to put it. In memory only — a
        /// parked comment does not survive an app restart.
        /// </summary>
        public string Comment
        {
            get { return comment; }
            set { Set(ref comment, value ?? ""); }
        }

        public EstimateUpdateMethods EstimateUpdateMethod
        {
            get { return estimateUpdateMethod; }
            set { Set(ref estimateUpdateMethod, value); }
        }

        public string EstimateUpdateValue
        {
            get { return estimateUpdateValue; }
            set { Set(ref estimateUpdateValue, value ?? ""); }
        }

        /// <summary>
        /// h:mm:ss — one format everywhere, including stopped rows, so a list never mixes
        /// "1:05" (which reads as 1 min 05 s) with "1:05:00".
        /// </summary>
        public string Elapsed
        {
            get
            {
                TimeSpan elapsed = timer.TimeElapsed;
                return string.Format("{0}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
            }
        }

        public bool HasTime
        {
            get { return timer.TimeElapsed.TotalSeconds >= 1; }
        }

        /// <summary>
        /// Nothing in the row yet — no issue, no time —, which is what an inline add that was
        /// abandoned leaves behind. Not saved, and removed without asking: there is nothing in
        /// it to lose. Not notified, so not for binding.
        /// </summary>
        internal bool IsEmpty
        {
            get { return !HasIssue && !HasTime; }
        }

        public bool IsRunning
        {
            get { return timer.Running; }
        }

        /// <summary>
        /// The row is held at the top of the list. Every view renders the entries in the same
        /// order, so pinning here puts it first in all four of them.
        /// </summary>
        public bool IsPinned
        {
            get { return isPinned; }
            set { Set(ref isPinned, value); }
        }

        /// <summary>Ordinal shown as the 1..9 keyboard hint. Set by <see cref="LedgerViewModel"/>.</summary>
        public int Index
        {
            get { return index; }
            set { Set(ref index, value); }
        }

        /// <summary>
        /// The row is showing its parent and subtask cells as combo boxes. Presentation only —
        /// the combos write straight through, so there is nothing to commit or roll back.
        /// </summary>
        public bool IsEditing
        {
            get { return isEditing; }
            set { Set(ref isEditing, value); }
        }

        /// <summary>Subtasks of the current parent, filled on demand while editing.</summary>
        public ObservableCollection<IssueItem> Subtasks { get; } = new ObservableCollection<IssueItem>();

        // Assigned by LedgerViewModel so every affordance triggers the same logic.
        public ICommand ToggleCommand { get; set; }
        public ICommand ResetCommand { get; set; }
        public ICommand LogCommand { get; set; }
        public ICommand RemoveCommand { get; set; }
        public ICommand EditCommand { get; set; }
        public ICommand EditTimeCommand { get; set; }
        public ICommand PinCommand { get; set; }
        public ICommand OpenInJiraCommand { get; set; }

        /// <summary>The clock advanced: only the readouts changed.</summary>
        public void NotifyTimeChanged()
        {
            Raise("Elapsed");
            Raise("HasTime");

            // Both are gated on HasTime, and nothing else re-queries them while the clock runs
            Refresh(ResetCommand);
            Refresh(LogCommand);
        }

        /// <summary>The timer was started, paused or reset: the row's whole state changed.</summary>
        public void NotifyTimerChanged()
        {
            NotifyTimeChanged();
            Raise("IsRunning");
        }

        private void RaiseIdentityChanged()
        {
            Raise("TimerKey");
            Raise("HasIssue");

            // "Open in Jira" is only available once the row points at an issue
            Refresh(OpenInJiraCommand);
        }

        private static void Refresh(ICommand command)
        {
            RelayCommand relay = command as RelayCommand;
            if (relay != null)
                relay.Refresh();
        }
    }
}
