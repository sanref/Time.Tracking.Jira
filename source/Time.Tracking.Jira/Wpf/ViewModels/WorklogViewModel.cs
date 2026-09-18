using Time.Tracking.Jira.Wpf.Infrastructure;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace Time.Tracking.Jira.Wpf.ViewModels
{
    /// <summary>
    /// Backs <see cref="Views.WorklogWindow"/>: what is being posted, plus the comment,
    /// start time and remaining-estimate choice. Does not call Jira — the caller does, after
    /// the dialog returns true, using <see cref="StartedAt"/> and the estimate fields as
    /// edited here.
    /// </summary>
    public class WorklogViewModel : ObservableObject
    {
        public WorklogViewModel(string issueKey, string issueSummary, DateTimeOffset startedAt, TimeSpan elapsed,
            string comment, EstimateUpdateMethods estimateUpdateMethod, string estimateUpdateValue)
        {
            IssueKey = issueKey;
            IssueSummary = issueSummary ?? "";
            Elapsed = elapsed;

            // Reopening a row parked earlier: leave room above the saved note for a new one,
            // same as v3's WorklogForm did with the comment it was constructed with.
            this.comment = string.IsNullOrEmpty(comment) ? "" : Environment.NewLine + Environment.NewLine + comment;

            DateTime local = startedAt.LocalDateTime;
            startDate = local.Date;
            startTimeText = local.ToString("HH:mm", CultureInfo.CurrentCulture);

            this.estimateUpdateMethod = estimateUpdateMethod;
            setToValue = estimateUpdateMethod == EstimateUpdateMethods.SetTo ? (estimateUpdateValue ?? "") : "";
            reduceByValue = estimateUpdateMethod == EstimateUpdateMethods.ManualDecrease ? (estimateUpdateValue ?? "") : "";
        }

        public string IssueKey { get; private set; }
        public string IssueSummary { get; private set; }
        public TimeSpan Elapsed { get; private set; }

        public string ElapsedText
        {
            get { return string.Format("{0}:{1:00}:{2:00}", (int)Elapsed.TotalHours, Elapsed.Minutes, Elapsed.Seconds); }
        }

        private string comment;
        public string Comment
        {
            get { return comment; }
            set { Set(ref comment, value ?? ""); }
        }

        private DateTime? startDate;
        public DateTime? StartDate
        {
            get { return startDate; }
            set { Set(ref startDate, value); }
        }

        private string startTimeText;
        public string StartTimeText
        {
            get { return startTimeText; }
            set { Set(ref startTimeText, value ?? ""); }
        }

        /// <summary>
        /// Date + time combined, or null when the typed time is not a valid 24-hour
        /// <c>HH:mm</c>. Deliberately stricter than <see cref="TimeSpan.TryParse(string)"/>,
        /// which would read "9" as nine days and "25:00" as twenty-five hours.
        /// </summary>
        public DateTime? StartedAt
        {
            get
            {
                if (startDate == null)
                    return null;

                Match time = Regex.Match((startTimeText ?? "").Trim(), @"^(\d{1,2}):(\d{2})$");
                if (!time.Success)
                    return null;

                int hours = int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture);
                int minutes = int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture);
                if (hours > 23 || minutes > 59)
                    return null;

                return startDate.Value.Date + new TimeSpan(hours, minutes, 0);
            }
        }

        private EstimateUpdateMethods estimateUpdateMethod;
        public EstimateUpdateMethods EstimateUpdateMethod
        {
            get { return estimateUpdateMethod; }
            set
            {
                if (!Set(ref estimateUpdateMethod, value))
                    return;

                Raise("IsAdjustAuto");
                Raise("IsLeaveUnchanged");
                Raise("IsSetTo");
                Raise("IsReduceBy");
            }
        }

        // Four bools instead of a converter: RadioButton.IsChecked binds straight to these.
        public bool IsAdjustAuto
        {
            get { return EstimateUpdateMethod == EstimateUpdateMethods.Auto; }
            set { if (value) EstimateUpdateMethod = EstimateUpdateMethods.Auto; }
        }

        public bool IsLeaveUnchanged
        {
            get { return EstimateUpdateMethod == EstimateUpdateMethods.Leave; }
            set { if (value) EstimateUpdateMethod = EstimateUpdateMethods.Leave; }
        }

        public bool IsSetTo
        {
            get { return EstimateUpdateMethod == EstimateUpdateMethods.SetTo; }
            set { if (value) EstimateUpdateMethod = EstimateUpdateMethods.SetTo; }
        }

        public bool IsReduceBy
        {
            get { return EstimateUpdateMethod == EstimateUpdateMethods.ManualDecrease; }
            set { if (value) EstimateUpdateMethod = EstimateUpdateMethods.ManualDecrease; }
        }

        private string setToValue = "";
        public string SetToValue
        {
            get { return setToValue; }
            set { Set(ref setToValue, value ?? ""); }
        }

        private string reduceByValue = "";
        public string ReduceByValue
        {
            get { return reduceByValue; }
            set { Set(ref reduceByValue, value ?? ""); }
        }

        /// <summary>The value to send Jira for the active mode, or null when it doesn't need one.</summary>
        public string EstimateUpdateValue
        {
            get
            {
                switch (EstimateUpdateMethod)
                {
                    case EstimateUpdateMethods.SetTo: return SetToValue;
                    case EstimateUpdateMethods.ManualDecrease: return ReduceByValue;
                    default: return null;
                }
            }
        }

        /// <summary>
        /// The field the active mode actually needs is empty or not a Jira time expression
        /// ("1h 30m"). Mirrors v3's WorklogForm.ValidateTimeInput; checked on submit rather
        /// than live, since the shared field chrome does not expose a hook for an inline
        /// invalid state.
        /// </summary>
        public bool IsEstimateValueValid
        {
            get
            {
                if (IsSetTo)
                    return !string.IsNullOrWhiteSpace(SetToValue) && JiraTimeHelpers.JiraTimeToTimeSpan(SetToValue) != null;

                if (IsReduceBy)
                    return !string.IsNullOrWhiteSpace(ReduceByValue) && JiraTimeHelpers.JiraTimeToTimeSpan(ReduceByValue) != null;

                return true;
            }
        }

        /// <summary>Set by the window so Ctrl+Enter and the Submit button run the same path.</summary>
        public ICommand SubmitCommand { get; set; }
    }
}
