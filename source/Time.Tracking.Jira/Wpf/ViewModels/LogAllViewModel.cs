using Time.Tracking.Jira.Wpf.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace Time.Tracking.Jira.Wpf.ViewModels
{
    /// <summary>Where one row of Log all stands.</summary>
    public enum LogAllRowState
    {
        Ready,
        Posting,
        Logged,
        Failed
    }

    /// <summary>
    /// Sends <paramref name="rows"/> to Jira off the UI thread, one after the other. Calls
    /// <paramref name="posted"/> on the UI thread as each one comes back — with Jira's error, or
    /// null when it went in — and <paramref name="done"/> once they all have.
    /// </summary>
    internal delegate void BatchPoster(IList<LogAllRow> rows, Action<LogAllRow, string> posted, Action done);

    /// <summary>One row of <see cref="Views.LogAllWindow"/>: a ledger row with time on it.</summary>
    public class LogAllRow : ObservableObject
    {
        private string comment;
        private bool isSelected;
        private LogAllRowState state = LogAllRowState.Ready;
        private string error = "";

        internal LogAllRow(TimeEntryViewModel entry)
        {
            Entry = entry;
            IssueKey = entry.TimerKey;
            IssueSummary = entry.TimerSummary;

            // Read once, as the one-row dialog does: what goes to Jira is what the clock holds
            // when the post is sent, and a running row keeps counting until then.
            Elapsed = entry.Timer.TimeElapsed;
            StartedAt = entry.Timer.GetInitialStartTime();

            comment = entry.Comment ?? "";
            BlockedReason = entry.HasIssue ? "" : "No issue yet. Pick one in the list to log this row.";
            EstimateNote = DescribeEstimate(entry.EstimateUpdateMethod, entry.EstimateUpdateValue);
            isSelected = !IsBlocked;
        }

        public TimeEntryViewModel Entry { get; private set; }

        public string IssueKey { get; private set; }
        public string IssueSummary { get; private set; }

        /// <summary>The key, or a placeholder for a row that has none yet.</summary>
        public string KeyText
        {
            get { return IssueKey.Length > 0 ? IssueKey : "No issue"; }
        }

        public TimeSpan Elapsed { get; private set; }

        public string ElapsedText
        {
            get { return string.Format("{0}:{1:00}:{2:00}", (int)Elapsed.TotalHours, Elapsed.Minutes, Elapsed.Seconds); }
        }

        /// <summary>When the worklog will say the work started. Changing it is the one-row Log's job.</summary>
        public DateTimeOffset StartedAt { get; private set; }

        public string StartedText
        {
            get { return "Started " + StartedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture); }
        }

        /// <summary>Why this row cannot be logged, or "" when it can.</summary>
        public string BlockedReason { get; private set; }

        public bool IsBlocked
        {
            get { return BlockedReason.Length > 0; }
        }

        /// <summary>A remaining-estimate choice parked with the row, spelled out; "" for the automatic one.</summary>
        public string EstimateNote { get; private set; }

        /// <summary>Starts as the row's draft — a note parked with "Save for later" — if it has one.</summary>
        public string Comment
        {
            get { return comment; }
            set { Set(ref comment, value ?? ""); }
        }

        public bool IsSelected
        {
            get { return isSelected; }
            set { Set(ref isSelected, value); }
        }

        public LogAllRowState State
        {
            get { return state; }
            set
            {
                if (!Set(ref state, value))
                    return;

                Raise("CanSelect");
                Raise("StatusText");
            }
        }

        /// <summary>Jira's reason when the post failed; "" otherwise.</summary>
        public string Error
        {
            get { return error; }
            set
            {
                if (Set(ref error, value ?? ""))
                    Raise("StatusText");
            }
        }

        /// <summary>Can be ticked and edited: it has an issue and has not gone in — or went and failed.</summary>
        public bool CanSelect
        {
            get { return !IsBlocked && (state == LogAllRowState.Ready || state == LogAllRowState.Failed); }
        }

        /// <summary>The line under the row once it has been sent: progress, success or Jira's reason.</summary>
        public string StatusText
        {
            get
            {
                switch (state)
                {
                    case LogAllRowState.Posting: return "Logging…";
                    case LogAllRowState.Logged: return "Logged ✓";
                    case LogAllRowState.Failed: return error;
                    default: return "";
                }
            }
        }

        private static string DescribeEstimate(EstimateUpdateMethods method, string value)
        {
            switch (method)
            {
                case EstimateUpdateMethods.Leave:
                    return "Remaining estimate: left unchanged";
                case EstimateUpdateMethods.SetTo:
                    return "Remaining estimate: set to " + value;
                case EstimateUpdateMethods.ManualDecrease:
                    return "Remaining estimate: reduced by " + value;
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// Backs <see cref="Views.LogAllWindow"/>: every row with time, reviewed and sent to Jira
    /// together instead of one dialog after another. It does not call Jira — the
    /// <see cref="BatchPoster"/> it is given does — so it can be exercised without one.
    /// </summary>
    public class LogAllViewModel : ObservableObject
    {
        private readonly BatchPoster poster;
        private bool isPosting;

        internal LogAllViewModel(IEnumerable<TimeEntryViewModel> entries, BatchPoster poster)
        {
            this.poster = poster;

            foreach (TimeEntryViewModel entry in entries)
            {
                LogAllRow row = new LogAllRow(entry);
                row.PropertyChanged += OnRowChanged;
                Rows.Add(row);
            }

            PostCommand = new RelayCommand(Post, CanPost);
        }

        public ObservableCollection<LogAllRow> Rows { get; } = new ObservableCollection<LogAllRow>();

        /// <summary>Sends the ticked rows. The button and Ctrl+Enter both run it.</summary>
        public ICommand PostCommand { get; private set; }

        public bool IsPosting
        {
            get { return isPosting; }
            private set
            {
                if (!Set(ref isPosting, value))
                    return;

                Raise("CanClose");
                Raise("FooterText");
                ((RelayCommand)PostCommand).Refresh();
            }
        }

        /// <summary>Not while sending: closing then would hide how the rows on their way ended.</summary>
        public bool CanClose
        {
            get { return !isPosting; }
        }

        /// <summary>"3 of 4 rows selected".</summary>
        public string SelectionText
        {
            get
            {
                return string.Format("{0} of {1} row{2} selected",
                    Selected().Count, Rows.Count, Rows.Count == 1 ? "" : "s");
            }
        }

        /// <summary>The time on the ticked rows, in the header's h/m format.</summary>
        public string SelectedTotalText
        {
            get
            {
                long total = 0;
                foreach (LogAllRow row in Selected())
                    total += (long)row.Elapsed.TotalSeconds;

                return string.Format("{0}h {1:00}m", total / 3600, (total % 3600) / 60);
            }
        }

        /// <summary>The hint before sending, progress during, the tally after.</summary>
        public string FooterText
        {
            get
            {
                if (isPosting)
                    return "Logging to Jira…";

                int logged = 0, failed = 0;
                foreach (LogAllRow row in Rows)
                {
                    if (row.State == LogAllRowState.Logged) logged++;
                    else if (row.State == LogAllRowState.Failed) failed++;
                }

                if (logged + failed == 0)
                    return "Press CTRL-Enter to log the selected rows";

                // Each failed row says why under itself; the footer only counts
                return failed == 0
                    ? string.Format("{0} logged", logged)
                    : string.Format("{0} logged · {1} failed", logged, failed);
            }
        }

        /// <summary>
        /// Called once the dialog has closed. A comment typed for a row that did not go in
        /// stays with that row, as its draft the next time it is logged.
        /// </summary>
        internal void KeepDrafts()
        {
            foreach (LogAllRow row in Rows)
            {
                // A logged row's draft went out with it, and a blocked row had no box
                if (row.State == LogAllRowState.Logged || row.IsBlocked)
                    continue;

                string typed = (row.Comment ?? "").Trim();
                if (typed != row.Entry.Comment)
                    row.Entry.Comment = typed;
            }
        }

        private List<LogAllRow> Selected()
        {
            List<LogAllRow> selected = new List<LogAllRow>();
            foreach (LogAllRow row in Rows)
                if (row.IsSelected && row.CanSelect)
                    selected.Add(row);

            return selected;
        }

        private bool CanPost()
        {
            return !isPosting && Selected().Count > 0;
        }

        private void Post()
        {
            if (!CanPost())
                return;

            List<LogAllRow> batch = Selected();
            foreach (LogAllRow row in batch)
            {
                row.Error = "";
                row.State = LogAllRowState.Posting;
            }

            IsPosting = true;
            poster(batch, OnPosted, OnDone);
        }

        private void OnPosted(LogAllRow row, string error)
        {
            if (error == null)
            {
                row.State = LogAllRowState.Logged;
                row.IsSelected = false;
                return;
            }

            // Jira gave no reason, which the user still has to be told apart from success
            row.Error = error.Length > 0 ? error : "Jira did not accept the worklog.";
            row.State = LogAllRowState.Failed;
        }

        private void OnDone()
        {
            IsPosting = false;
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsSelected" && e.PropertyName != "State")
                return;

            Raise("SelectionText");
            Raise("SelectedTotalText");
            Raise("FooterText");
            ((RelayCommand)PostCommand).Refresh();
        }
    }
}
