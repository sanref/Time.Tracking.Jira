using Time.Tracking.Jira.Logging;
using Time.Tracking.Jira.Wpf.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace Time.Tracking.Jira.Wpf.ViewModels
{
    /// <summary>Which of the four layouts renders the entries. Picked in Settings.</summary>
    public enum ViewMode
    {
        Ledger,
        Cards,
        Focus,
        Grid
    }

    /// <summary>
    /// The whole tracker: the rows, the one ticker that drives them, the Jira session and the
    /// persistence of both. All four views are projections of this — no view owns state.
    /// </summary>
    public class LedgerViewModel : ObservableObject
    {
        private const int TickIntervalMs = 1000;

        // The rows reach settings.json on close too, but a crash, Task Manager or a power cut
        // never gets that far: this bounds what any of them can take to the last minute.
        private const int AutosaveIntervalMs = 60 * 1000;

        // Below this a search is not worth a round trip to Jira — and the combo's own filter
        // is already showing whatever the loaded list has.
        private const int MinSearchLength = 3;

        private const string NoConnectionMessage =
            "No Jira connection available. Please verify the configuration and connection before logging work.";

        // Waits between tries while Jira cannot be reached: quick at first, for a VPN that is
        // still coming up, then every five minutes for as long as it takes.
        private static readonly int[] ReconnectDelaysSeconds = { 15, 30, 60, 120, 300 };

        // After a network change: the event comes in bursts, and an adapter can have an
        // address a moment before it can reach anything.
        private static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(3);

        private readonly Settings settings;
        private readonly IDialogService dialogs;
        private readonly JiraSession session;
        private readonly DispatcherTimer ticker;
        private readonly DispatcherTimer autosaver;
        private readonly DispatcherTimer reconnector;
        private readonly Dispatcher dispatcher;

        private readonly IssueCatalog catalog;

        // Bound by the inline editor and the Add issue dialog, so it has to notify
        private readonly ObservableCollection<IssueItem> parentIssues = new ObservableCollection<IssueItem>();

        // Issues the pickers found in Jira rather than in the assigned list. Kept aside
        // because reloading that list clears everything, and these are not in it.
        private readonly List<IssueItem> foundIssues = new List<IssueItem>();

        private TimeEntryViewModel runningEntry;
        private ViewMode viewMode;
        private string connectionStatus = "Not configured";
        private bool isConnected;

        // Rows a session lock paused, for the unlock to resume. The rows themselves, not their
        // keys: a row with no issue yet has an empty key, and two rows can share one.
        private readonly List<TimeEntryViewModel> pausedOnLock = new List<TimeEntryViewModel>();
        private bool systemEventsSubscribed;

        // Bumped by every connection attempt: an answer that comes back after a newer attempt
        // started — Settings accepted, the status clicked — is stale and dropped.
        private int connectAttempt;
        private bool connecting;
        private bool networkChangedWhileConnecting;

        // Failed tries in a row since Jira was last reachable, which sets the next wait
        private int reconnectAttempts;

        // Internal because Settings is: the window builds this, never XAML.
        internal LedgerViewModel(Settings settings, IDialogService dialogs)
        {
            this.settings = settings;
            this.dialogs = dialogs;
            viewMode = ToViewMode(settings.StartupForm);
            dispatcher = Dispatcher.CurrentDispatcher;

            session = new JiraSession(settings.JiraBaseUrl);
            catalog = new IssueCatalog(session);

            AddIssueCommand = new RelayCommand(AddIssue);
            AddInlineCommand = new RelayCommand(AddInline);
            LogAllCommand = new RelayCommand(LogAll, AnyEntryHasTime);
            SettingsCommand = new RelayCommand(EditSettings);
            RefreshCommand = new RelayCommand(RefreshFromJira, () => session.SessionValid);
            ReconnectCommand = new RelayCommand(Reconnect);
            FocusCommand = new RelayCommand(RaiseFocusRequested, () => runningEntry != null);

            PauseResumeCommand = new RelayCommand(
                () => { if (runningEntry != null) Toggle(runningEntry); },
                () => runningEntry != null);
            ResetRunningCommand = new RelayCommand(
                () => { if (runningEntry != null) Reset(runningEntry); },
                () => runningEntry != null);
            LogRunningCommand = new RelayCommand(
                () => { if (runningEntry != null) Log(runningEntry); },
                () => runningEntry != null);
            StartByIndexCommand = new RelayCommand(StartByIndex);
            SwitchViewCommand = new RelayCommand(p => SwitchView((ViewMode)p));

            ticker = new DispatcherTimer(DispatcherPriority.Normal);
            ticker.Interval = TimeSpan.FromMilliseconds(TickIntervalMs);
            ticker.Tick += OnTick;

            // Background: a write can wait behind input and rendering, it only has to happen
            autosaver = new DispatcherTimer(DispatcherPriority.Background);
            autosaver.Interval = TimeSpan.FromMilliseconds(AutosaveIntervalMs);
            autosaver.Tick += OnAutosaveTick;

            // One-shot: started only while Jira is unreachable, with the wait for the next try
            reconnector = new DispatcherTimer(DispatcherPriority.Background);
            reconnector.Tick += OnReconnectTick;
        }

        #region public surface

        public ObservableCollection<TimeEntryViewModel> Entries { get; } = new ObservableCollection<TimeEntryViewModel>();

        /// <summary>Everything except the running entry — the Focus view's queue.</summary>
        public ObservableCollection<TimeEntryViewModel> Queue { get; } = new ObservableCollection<TimeEntryViewModel>();

        /// <summary>
        /// The layout in use. Changing it in Settings switches live; only crossing to or from
        /// the classic WinForms window needs a restart.
        /// </summary>
        public ViewMode ViewMode
        {
            get { return viewMode; }
            private set { Set(ref viewMode, value); }
        }

        public ICommand AddIssueCommand { get; private set; }

        /// <summary>Adds a row straight in the grid, without the dialog.</summary>
        public ICommand AddInlineCommand { get; private set; }

        public ICommand LogAllCommand { get; private set; }
        public ICommand SettingsCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand ReconnectCommand { get; private set; }
        public ICommand FocusCommand { get; private set; }
        public ICommand PauseResumeCommand { get; private set; }
        public ICommand ResetRunningCommand { get; private set; }
        public ICommand LogRunningCommand { get; private set; }
        public ICommand StartByIndexCommand { get; private set; }

        /// <summary>Parameter is the target <see cref="ViewMode"/> — the footer's quick switcher.</summary>
        public ICommand SwitchViewCommand { get; private set; }

        /// <summary>The focus button was pressed; the window owns the mini window's lifetime.</summary>
        public event EventHandler FocusRequested;

        /// <summary>The settings dialog was accepted; window-level options may have changed.</summary>
        public event EventHandler SettingsApplied;

        /// <summary>The layout changed — from Settings or from the footer's quick switcher.</summary>
        public event EventHandler ViewModeChanged;

        public TimeEntryViewModel RunningEntry
        {
            get { return runningEntry; }
        }

        public bool HasRunning
        {
            get { return runningEntry != null; }
        }

        public bool IsConnected
        {
            get { return isConnected; }
            private set { Set(ref isConnected, value); }
        }

        public string ConnectionStatus
        {
            get { return connectionStatus; }
            private set { Set(ref connectionStatus, value); }
        }

        /// <summary>
        /// Sum of every row. Coarse on purpose — seconds only matter on the running row.
        /// </summary>
        public string TotalToday
        {
            get
            {
                long total = 0;
                foreach (TimeEntryViewModel entry in Entries)
                    total += (long)entry.Timer.TimeElapsed.TotalSeconds;

                return string.Format("{0}h {1:00}m", total / 3600, (total % 3600) / 60);
            }
        }

        public string PendingSummary
        {
            get
            {
                int pending = 0;
                foreach (TimeEntryViewModel entry in Entries)
                    if (entry.HasTime) pending++;

                string running = runningEntry == null ? "none running" : "1 running";
                return string.Format("{0} subtasks · {1} · {2} pending upload", Entries.Count, running, pending);
            }
        }

        /// <summary>Issues assigned to the current user, offered by both issue pickers.</summary>
        public ObservableCollection<IssueItem> ParentIssues
        {
            get { return parentIssues; }
        }

        /// <summary>
        /// The pickers' way out of "it is not in my list": what the search does when nothing
        /// in <see cref="ParentIssues"/> matches. Its hits join that list, so the combo that
        /// asked can offer them like any other issue.
        /// </summary>
        public IssueLookup IssueLookup
        {
            get { return LookupIssues; }
        }

        internal JiraClient Jira
        {
            get { return session.Client; }
        }

        #endregion

        #region lifetime

        /// <summary>Called once the window is shown.</summary>
        public void Initialize()
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            systemEventsSubscribed = true;

            // First, because it explains what comes next: the defaults are in, so Settings opens
            if (settings.CorruptCopy != null)
            {
                dialogs.Warn(string.Format(
                    "Your settings could not be read, so Time.Tracking.Jira started with the default ones.{0}{0}" +
                    "The file was kept as:{0}{1}{0}{0}Any time you had not logged yet is in that copy.",
                    Environment.NewLine, settings.CorruptCopy),
                    "Settings Could Not Be Read");
            }

            if (settings.FirstRun)
            {
                settings.FirstRun = false;
                EditSettings();
            }
            else if (IsJiraEnabled)
            {
                AuthenticateJira();
            }
            else
            {
                dialogs.Info("Please configure the Jira connection using the Settings button",
                    "Configuration Required");
            }

            LoadPersistedRows();

            // Only from here on: until the saved rows are loaded Entries holds none of them,
            // and a save would write that empty list over the file that has them.
            dispatcher.UnhandledException += OnDispatcherUnhandledException;
            autosaver.Start();

            ticker.Start();
        }

        /// <summary>Called while the window is closing.</summary>
        public void Shutdown()
        {
            ticker.Stop();
            autosaver.Stop();
            reconnector.Stop();
            dispatcher.UnhandledException -= OnDispatcherUnhandledException;

            // Static events: left subscribed, they would hold this view model alive
            if (systemEventsSubscribed)
            {
                Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
                NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
                NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                systemEventsSubscribed = false;
            }

            SavePersistedRows(false);
        }

        private bool IsJiraEnabled
        {
            get
            {
                return !(
                    string.IsNullOrWhiteSpace(settings.JiraBaseUrl) ||
                    string.IsNullOrWhiteSpace(settings.Username) ||
                    string.IsNullOrWhiteSpace(settings.Password)
                );
            }
        }

        #endregion

        #region timer engine

        private void OnTick(object sender, EventArgs e)
        {
            // Only the running rows move; the others were refreshed when they last changed.
            foreach (TimeEntryViewModel entry in Entries)
                if (entry.IsRunning)
                    entry.NotifyTimeChanged();

            AfterTotalsChanged();
        }

        /// <summary>
        /// Starts or pauses the row. The issue is deliberately not required here: you often
        /// know you started working before you know what to book it against, so the clock
        /// runs first and <see cref="Log"/> is where the issue becomes mandatory.
        /// </summary>
        public void Toggle(TimeEntryViewModel entry)
        {
            if (entry.IsRunning)
            {
                entry.Timer.Pause();
            }
            else
            {
                if (!settings.AllowMultipleTimers)
                    PauseAllExcept(entry);

                entry.Timer.Start();
            }

            entry.NotifyTimerChanged();
            UpdateRunningEntry();
            AfterTotalsChanged();
        }

        public void Reset(TimeEntryViewModel entry)
        {
            if (!entry.HasTime)
                return;

            if (!dialogs.Confirm(
                    string.Format("Are you sure you want to reset the timer for {0}?", entry.TimerKey),
                    "Confirm Reset"))
                return;

            entry.Timer.Reset();
            entry.NotifyTimerChanged();
            UpdateRunningEntry();
            AfterTotalsChanged();
        }

        /// <summary>
        /// Drops the row from the ledger only. It never touches Jira. Asks first unless the row
        /// is empty: there is nothing in it to lose, and a confirmation there only gets in the
        /// way of clearing out a row added by mistake.
        /// </summary>
        public void Remove(TimeEntryViewModel entry)
        {
            if (!entry.IsEmpty)
            {
                string message = entry.HasIssue
                    ? string.Format("Are you sure you want to delete the row for {0}?", entry.TimerKey)
                    : "Are you sure you want to delete this row?";

                if (!dialogs.Confirm(message, "Confirm Deletion"))
                    return;
            }

            Entries.Remove(entry);
            Reindex();
            RebuildQueue();
            UpdateRunningEntry();
            AfterTotalsChanged();
        }

        /// <summary>
        /// Pins the row to the top, or lets it back down. The pin is applied by moving the row
        /// inside <see cref="Entries"/>, which is the order all four views render and the order
        /// the rows are saved in — so one pin holds across layouts, across the 1..9 shortcuts
        /// and across restarts, without any view having to sort for itself.
        /// </summary>
        public void TogglePin(TimeEntryViewModel entry)
        {
            entry.IsPinned = !entry.IsPinned;

            SortPinnedFirst();
            Reindex();
            RebuildQueue();
        }

        /// <summary>
        /// Moves the pinned rows to the head of the list, in the order they already had. The
        /// unpinned ones keep theirs too: pinning is meant to lift one row, not to reshuffle
        /// the list around it.
        /// </summary>
        private void SortPinnedFirst()
        {
            int target = 0;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (!Entries[i].IsPinned)
                    continue;

                if (i != target)
                    Entries.Move(i, target);

                target++;
            }
        }

        private void PauseAllExcept(TimeEntryViewModel entry)
        {
            foreach (TimeEntryViewModel other in Entries)
            {
                if (other == entry || !other.IsRunning)
                    continue;

                other.Timer.Pause();
                other.NotifyTimerChanged();
            }
        }

        private void StartByIndex(object parameter)
        {
            int position;
            if (parameter == null || !int.TryParse(parameter.ToString(), out position))
                return;

            if (position < 1 || position > Entries.Count)
                return;

            Toggle(Entries[position - 1]);
        }

        private void UpdateRunningEntry()
        {
            TimeEntryViewModel running = null;
            foreach (TimeEntryViewModel entry in Entries)
            {
                if (entry.IsRunning)
                {
                    running = entry;
                    break;
                }
            }

            if (running != runningEntry)
            {
                runningEntry = running;
                Raise("RunningEntry");
                Raise("HasRunning");
                RebuildQueue();
            }

            ((RelayCommand)PauseResumeCommand).Refresh();
            ((RelayCommand)ResetRunningCommand).Refresh();
            ((RelayCommand)LogRunningCommand).Refresh();
            ((RelayCommand)FocusCommand).Refresh();
        }

        private void AfterTotalsChanged()
        {
            Raise("TotalToday");
            Raise("PendingSummary");
            ((RelayCommand)LogAllCommand).Refresh();
        }

        private void Reindex()
        {
            for (int i = 0; i < Entries.Count; i++)
                Entries[i].Index = i + 1;
        }

        private void RebuildQueue()
        {
            Queue.Clear();
            foreach (TimeEntryViewModel entry in Entries)
                if (entry != runningEntry)
                    Queue.Add(entry);
        }

        private bool AnyEntryHasTime()
        {
            foreach (TimeEntryViewModel entry in Entries)
                if (entry.HasTime && entry.HasIssue)
                    return true;

            return false;
        }

        internal static ViewMode ToViewMode(StartupFormSetting setting)
        {
            switch (setting)
            {
                case StartupFormSetting.Cards: return ViewMode.Cards;
                case StartupFormSetting.Focus: return ViewMode.Focus;
                case StartupFormSetting.Grid: return ViewMode.Grid;
                default: return ViewMode.Ledger;
            }
        }

        private static StartupFormSetting ToStartupFormSetting(ViewMode mode)
        {
            switch (mode)
            {
                case ViewMode.Cards: return StartupFormSetting.Cards;
                case ViewMode.Focus: return StartupFormSetting.Focus;
                case ViewMode.Grid: return StartupFormSetting.Grid;
                default: return StartupFormSetting.Ledger;
            }
        }

        /// <summary>
        /// The footer's quick switcher. Also updates the startup default — same as Settings'
        /// "Startup form" combo does today — so the layout you last picked is the one that
        /// greets you next time, rather than reverting silently on restart.
        /// </summary>
        private void SwitchView(ViewMode mode)
        {
            if (ViewMode == mode)
                return;

            ViewMode = mode;
            settings.StartupForm = ToStartupFormSetting(mode);

            EventHandler handler = ViewModeChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        #endregion

        #region rows

        public void AddEntry(TimeEntryViewModel entry)
        {
            entry.ToggleCommand = new RelayCommand(() => Toggle(entry));
            entry.ResetCommand = new RelayCommand(() => Reset(entry), () => entry.HasTime);
            entry.LogCommand = new RelayCommand(() => Log(entry), () => entry.HasTime);
            entry.RemoveCommand = new RelayCommand(() => Remove(entry));
            entry.EditCommand = new RelayCommand(() => EditIssue(entry));
            entry.EditTimeCommand = new RelayCommand(() => EditTime(entry));
            entry.PinCommand = new RelayCommand(() => TogglePin(entry));
            entry.OpenInJiraCommand = new RelayCommand(() => OpenInJira(entry), () => CanOpenInJira(entry));

            Entries.Add(entry);
            Reindex();
            RebuildQueue();
            AfterTotalsChanged();
        }

        private void AddIssue()
        {
            IssueSelection picked = dialogs.PickIssue(this, null);
            if (picked == null)
                return;

            TimeEntryViewModel entry = new TimeEntryViewModel(
                picked.ParentKey, picked.ParentSummary, picked.SubtaskKey, picked.SubtaskSummary);

            AddEntry(entry);
            RememberParentSummary(entry.ParentKey, entry.ParentSummary);
        }

        /// <summary>
        /// Adds an empty row and opens its cells right away — the grid's own way in, without
        /// going through the Add issue dialog.
        /// </summary>
        private void AddInline()
        {
            TimeEntryViewModel entry = new TimeEntryViewModel("", "", "", "");
            AddEntry(entry);
            BeginEdit(entry);
        }

        private void EditIssue(TimeEntryViewModel entry)
        {
            IssueSelection picked = dialogs.PickIssue(this, entry);
            if (picked == null)
                return;

            entry.ParentKey = picked.ParentKey;
            entry.ParentSummary = picked.ParentSummary;
            entry.SubtaskKey = picked.SubtaskKey;
            entry.SubtaskSummary = picked.SubtaskSummary;

            RememberParentSummary(entry.ParentKey, entry.ParentSummary);
            UpdateRunningEntry();
        }

        /// <summary>Sets the row's clock by hand. The dialog validates against JiraTimeHelpers.</summary>
        private void EditTime(TimeEntryViewModel entry)
        {
            TimeSpan? edited = dialogs.EditTime(entry.TimerKey, entry.TimerSummary, entry.Timer.TimeElapsed);
            if (edited == null)
                return;

            entry.Timer.TimeElapsed = edited.Value;

            entry.NotifyTimerChanged();
            AfterTotalsChanged();
        }

        private bool CanOpenInJira(TimeEntryViewModel entry)
        {
            return entry.HasIssue && !string.IsNullOrWhiteSpace(settings.JiraBaseUrl);
        }

        /// <summary>
        /// Opens the row's parent task in the browser — not the subtask, which is where the
        /// worklog goes but rarely what you want to look at. A row with no parent falls back to
        /// whatever key it has. The URL is built the way v3 built it: base URL + <c>browse/</c> + key.
        /// </summary>
        private void OpenInJira(TimeEntryViewModel entry)
        {
            if (!CanOpenInJira(entry))
                return;

            string key = string.IsNullOrEmpty(entry.ParentKey) ? entry.TimerKey : entry.ParentKey;

            string baseUrl = settings.JiraBaseUrl.Trim();
            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                baseUrl += "/";

            string url = baseUrl + "browse/" + key.Trim();

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Could not open {0} in the browser: {1}", url, ex.Message));
            }
        }

        #endregion

        #region inline editing

        /// <summary>Opens a row's cells as combo boxes. Only one row edits at a time.</summary>
        public void BeginEdit(TimeEntryViewModel entry)
        {
            foreach (TimeEntryViewModel other in Entries)
                other.IsEditing = other == entry;

            if (entry != null && !string.IsNullOrEmpty(entry.ParentKey) && entry.Subtasks.Count == 0)
                ReloadSubtasks(entry);
        }

        public void EndEdit(TimeEntryViewModel entry)
        {
            if (entry != null)
                entry.IsEditing = false;
        }

        /// <summary>
        /// The parent picked in a row's combo. The subtask always goes with its parent, so
        /// changing one clears the other and re-queries the list.
        /// </summary>
        public void SetParent(TimeEntryViewModel entry, string key, string summary)
        {
            key = (key ?? "").Trim();
            if (entry.ParentKey == key)
                return;

            entry.ParentKey = key;
            entry.ParentSummary = string.IsNullOrEmpty(summary) ? LookupParentSummary(key) : summary;
            entry.SubtaskKey = "";
            entry.SubtaskSummary = "";

            RememberParentSummary(key, summary);

            // Typed by hand and unknown so far: ask Jira for its title
            if (string.IsNullOrEmpty(entry.ParentSummary))
                RequestIssueSummary(key);

            ReloadSubtasks(entry);
            AfterRowIdentityChanged();
        }

        public void SetSubtask(TimeEntryViewModel entry, string key, string summary)
        {
            key = (key ?? "").Trim();
            if (entry.SubtaskKey == key)
                return;

            entry.SubtaskKey = key;
            entry.SubtaskSummary = summary ?? "";
            AfterRowIdentityChanged();
        }

        private void ReloadSubtasks(TimeEntryViewModel entry)
        {
            entry.Subtasks.Clear();
            string parentKey = entry.ParentKey;

            FetchSubtasks(parentKey, (subtasks, error) =>
            {
                // The user may have moved on while the query was in flight
                if (entry.ParentKey != parentKey)
                    return;

                entry.Subtasks.Clear();
                foreach (IssueItem item in subtasks)
                    entry.Subtasks.Add(item);
            });
        }

        /// <summary>
        /// Runs the "subtasks of X" query off the UI thread and calls back on it. Shared by
        /// the Add issue dialog and the ledger's inline editor.
        /// </summary>
        internal void FetchSubtasks(string parentKey, Action<List<IssueItem>, string> onDone)
        {
            if (string.IsNullOrEmpty(parentKey) || !session.SessionValid)
            {
                onDone(new List<IssueItem>(), session.SessionValid ? null : "Not connected to Jira");
                return;
            }

            Task.Factory.StartNew(() =>
            {
                string error;
                List<IssueItem> subtasks = catalog.Subtasks(parentKey, out error);

                OnUiThread(() => onDone(subtasks, error));
            });
        }

        private string LookupParentSummary(string key)
        {
            return catalog.Lookup(key);
        }

        /// <summary>A row now points at a different issue, so the running row and totals may differ.</summary>
        private void AfterRowIdentityChanged()
        {
            UpdateRunningEntry();
            AfterTotalsChanged();
        }

        #endregion

        #region worklog

        public void Log(TimeEntryViewModel entry)
        {
            // The one place an issue is required: a timer may run untagged, a worklog cannot
            if (!entry.HasIssue)
            {
                dialogs.Warn("This row has no issue yet. Pick one before logging its time to Jira.", "Warning");
                return;
            }

            if (!session.SessionValid)
            {
                dialogs.Error(NoConnectionMessage, "Connection Error");
                return;
            }

            if (entry.Timer.TimeElapsedNearestMinute.TotalMinutes < 1)
            {
                dialogs.Warn("Time must be at least 1 minute to log", "Warning");
                return;
            }

            WorklogViewModel worklogVm = new WorklogViewModel(
                entry.TimerKey, entry.TimerSummary,
                entry.Timer.GetInitialStartTime(), entry.Timer.TimeElapsedNearestMinute,
                entry.Comment, entry.EstimateUpdateMethod, entry.EstimateUpdateValue);

            WorklogChoice choice = dialogs.Worklog(worklogVm);

            if (choice != WorklogChoice.Post)
            {
                // CouldNotOpen means the window never appeared, so there is nothing to park.
                if (choice == WorklogChoice.SaveForLater)
                {
                    // Parking it: the note carries a timestamp so a later reopen shows when it
                    // was set aside, same as v3's "Save for later" did.
                    entry.Comment = string.Format("{0}:{1}{2}",
                        DateTime.Now.ToString("g"), Environment.NewLine, (worklogVm.Comment ?? "").Trim());
                    entry.EstimateUpdateMethod = worklogVm.EstimateUpdateMethod;
                    entry.EstimateUpdateValue = worklogVm.EstimateUpdateValue;
                }

                return;
            }

            entry.Comment = (worklogVm.Comment ?? "").Trim();
            entry.EstimateUpdateMethod = worklogVm.EstimateUpdateMethod;
            entry.EstimateUpdateValue = worklogVm.EstimateUpdateValue;

            PostWorklogToJira(entry, entry.Comment, worklogVm.StartedAt.Value, entry.EstimateUpdateMethod, entry.EstimateUpdateValue);
        }

        /// <summary>
        /// Every row with time, reviewed and posted from one dialog. Rows with no issue are
        /// listed too, disabled, so none is left out without the user seeing it.
        /// </summary>
        private void LogAll()
        {
            if (!session.SessionValid)
            {
                dialogs.Error(NoConnectionMessage, "Connection Error");
                return;
            }

            List<TimeEntryViewModel> withTime = new List<TimeEntryViewModel>();
            foreach (TimeEntryViewModel entry in Entries)
                if (entry.HasTime)
                    withTime.Add(entry);

            LogAllViewModel vm = new LogAllViewModel(withTime, PostBatch);
            dialogs.LogAll(vm);

            // A comment typed for a row that did not go out stays with the row
            vm.KeepDrafts();
        }

        private void PostWorklogToJira(TimeEntryViewModel entry, string comment, DateTimeOffset startTime,
            EstimateUpdateMethods estimateUpdateMethod, string estimateUpdateValue)
        {
            string issueKey = entry.TimerKey;
            WatchTimer timer = entry.Timer;

            // So the success callback can tell whether this row's timer was reset (and possibly
            // restarted) while the post was in flight — see SettlePosted.
            int timerGeneration = timer.Generation;

            Task.Factory.StartNew(() =>
            {
                try
                {
                    TimeSpan postedDuration = timer.TimeElapsedNearestMinute;

                    string error;
                    bool posted = session.Client.PostWorklog(
                        issueKey,
                        startTime,
                        postedDuration,
                        comment,
                        estimateUpdateMethod,
                        estimateUpdateValue,
                        out error);

                    OnUiThread(() =>
                    {
                        if (posted)
                        {
                            SettlePosted(entry, timerGeneration, postedDuration, comment);
                            return;
                        }

                        Logger.Instance.Log(string.Format("Error posting worklog for {0}: {1}", issueKey, error));
                        dialogs.Error(string.Format("Error posting worklog for {0}:{1}{1}{2}", issueKey, Environment.NewLine, error),
                            "Error");
                    });
                }
                catch (Exception ex)
                {
                    OnUiThread(() =>
                    {
                        Logger.Instance.Log(string.Format("Exception posting worklog for {0}: {1}", issueKey, ex.Message));
                        dialogs.Error(string.Format("Error posting worklog for {0}:{1}{1}{2}", issueKey, Environment.NewLine, ex.Message),
                            "Error");
                    });
                }
            });
        }

        /// <summary>
        /// Books a worklog Jira accepted against its row. UI thread; shared by the one-row Log
        /// and Log all.
        /// </summary>
        /// <param name="timerGeneration">The timer's <see cref="WatchTimer.Generation"/> when the post was fired.</param>
        /// <param name="postedComment">The comment that went out with it.</param>
        internal void SettlePosted(TimeEntryViewModel entry, int timerGeneration, TimeSpan postedDuration, string postedComment)
        {
            WatchTimer timer = entry.Timer;

            // If the row was reset while the post was in flight, it may already be tracking a
            // brand-new session (the user reset and restarted it before the post came back).
            // That session was never posted, so a blind timer.Reset() here would wipe it out —
            // only touch the timer if nothing has reset it since this post was fired.
            if (timer.Generation == timerGeneration)
            {
                TimeSpan remaining = timer.TimeElapsed - postedDuration;
                if (remaining > TimeSpan.Zero)
                {
                    // Some time ticked away on the running row during the round trip. Keep it —
                    // subtracting only what was posted — but pause: leaving it running would
                    // silently carry that time into a new, un-posted session under the same click.
                    timer.TimeElapsed = remaining;
                    if (timer.Running)
                        timer.Pause();
                }
                else
                {
                    // The clock is spent. Reset outright rather than just zeroing it, so the next
                    // Start counts as a fresh session and the worklog it posts is dated from then —
                    // not from the start of the session that was just logged.
                    timer.Reset();
                }
            }

            // The draft went out with this worklog, so it is spent, as v3 treated it: left in
            // place it would come back as a parked note the next time the row is logged. Unless
            // it changed while the post was in flight — then it is a newer note, not this one.
            if (entry.Comment == (postedComment ?? ""))
            {
                entry.Comment = "";
                entry.EstimateUpdateMethod = EstimateUpdateMethods.Auto;
                entry.EstimateUpdateValue = "";
            }

            entry.NotifyTimerChanged();
            UpdateRunningEntry();
            AfterTotalsChanged();
        }

        /// <summary>
        /// Log all's send. The rows go one after the other on a single background task — one
        /// click is not a reason to hit Jira with all of them at once — and each reports back
        /// as soon as Jira has answered for it.
        /// </summary>
        private void PostBatch(IList<LogAllRow> rows, Action<LogAllRow, string> posted, Action done)
        {
            // Everything a post needs is read here, on the UI thread the rows belong to
            List<Action> sends = new List<Action>();

            foreach (LogAllRow row in rows)
            {
                LogAllRow current = row;
                TimeEntryViewModel entry = row.Entry;
                WatchTimer timer = entry.Timer;
                int timerGeneration = timer.Generation;
                string issueKey = entry.TimerKey;
                string comment = (row.Comment ?? "").Trim();
                DateTimeOffset startTime = row.StartedAt;
                EstimateUpdateMethods estimateUpdateMethod = entry.EstimateUpdateMethod;
                string estimateUpdateValue = entry.EstimateUpdateValue;

                // As in the one-row Log, the draft becomes what is being sent: a failure keeps it
                // for the next try, a success clears it (SettlePosted).
                entry.Comment = comment;

                sends.Add(() =>
                {
                    TimeSpan postedDuration = TimeSpan.Zero;
                    string error;
                    bool ok;

                    // Whatever goes wrong with one row is that row's failure, not the batch's
                    try
                    {
                        postedDuration = timer.TimeElapsedNearestMinute;
                        ok = session.Client.PostWorklog(issueKey, startTime, postedDuration, comment,
                            estimateUpdateMethod, estimateUpdateValue, out error);
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        error = ex.Message;
                    }

                    OnUiThread(() =>
                    {
                        if (ok)
                            SettlePosted(entry, timerGeneration, postedDuration, comment);
                        else
                            Logger.Instance.Log(string.Format("Error posting worklog for {0}: {1}", issueKey, error));

                        posted(current, ok ? null : error);
                    });
                });
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    foreach (Action send in sends)
                        send();
                }
                finally
                {
                    // Always: the dialog refuses to close until it hears this
                    OnUiThread(done);
                }
            });
        }

        #endregion

        #region jira session

        private void Reconnect()
        {
            if (!IsJiraEnabled)
            {
                dialogs.Info("Please configure the Jira connection using the Settings button",
                    "Configuration Required");
                return;
            }

            if (session.SessionValid)
            {
                dialogs.Info("You are already connected to Jira", "Information");
                return;
            }

            AuthenticateJira();
        }

        /// <param name="retry">
        /// One of the automatic retries, which keeps the back-off where it was. Anything else —
        /// startup, Settings, a click on the status — starts it over.
        /// </param>
        private void AuthenticateJira(bool retry = false)
        {
            string username = settings.Username;
            string password = settings.Password;

            // A new attempt supersedes the retry that was waiting and any answer still on its way
            int attempt = ++connectAttempt;
            reconnector.Stop();
            connecting = true;
            networkChangedWhileConnecting = false;

            if (!retry)
                reconnectAttempts = 0;

            SetConnection(false, "Connecting...");

            Task.Factory.StartNew(() =>
            {
                SessionResult result = session.Connect(username, password);

                OnUiThread(() =>
                {
                    if (attempt != connectAttempt)
                        return;

                    connecting = false;

                    switch (result.State)
                    {
                        case SessionState.Connected:
                            reconnectAttempts = 0;
                            SetConnection(true, "Connected");
                            LoadAssignedParentIssues();
                            ResolveMissingSummaries();
                            break;

                        case SessionState.Unreachable:
                            // Short on purpose: the Cards header was measured with "Session invalid"
                            SetConnection(false, "Offline");
                            ScheduleReconnect();
                            break;

                        case SessionState.SessionInvalid:
                            SetConnection(false, "Session invalid");
                            break;

                        default:
                            // A refused login is not retried: it stays refused, and repeating it
                            // against Atlassian can end in a CAPTCHA or a locked account.
                            SetConnection(false, "Not connected");
                            break;
                    }
                });
            });
        }

        /// <summary>
        /// The wait before the next try, after <paramref name="failedAttempts"/> tries in a row
        /// could not reach Jira.
        /// </summary>
        internal static TimeSpan ReconnectDelay(int failedAttempts)
        {
            int index = Math.Max(0, Math.Min(failedAttempts, ReconnectDelaysSeconds.Length - 1));
            return TimeSpan.FromSeconds(ReconnectDelaysSeconds[index]);
        }

        /// <summary>
        /// Jira could not be reached: try again after the next wait in the back-off — or right
        /// away if the network changed while that attempt was out, since it may have failed on a
        /// network that was still coming up.
        /// </summary>
        private void ScheduleReconnect()
        {
            reconnector.Interval = networkChangedWhileConnecting ? NetworkSettleDelay : ReconnectDelay(reconnectAttempts);
            networkChangedWhileConnecting = false;
            reconnectAttempts++;
            reconnector.Start();

            Logger.Instance.Log(string.Format("Jira could not be reached; trying again in {0:0} s",
                reconnector.Interval.TotalSeconds));
        }

        /// <summary>Stops trying: the connection is no longer wanted, or is being set up anew.</summary>
        private void CancelConnect()
        {
            connectAttempt++;
            connecting = false;
            reconnector.Stop();
        }

        private void OnReconnectTick(object sender, EventArgs e)
        {
            reconnector.Stop();

            // Settings may have changed meanwhile, or a click on the status got there first
            if (!IsJiraEnabled || session.SessionValid)
                return;

            AuthenticateJira(true);
        }

        // Raised on a pool thread, often several times for one change
        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            OnUiThread(OnNetworkChanged);
        }

        private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable)
                OnUiThread(OnNetworkChanged);
        }

        /// <summary>
        /// A network came up or changed — a cable, Wi-Fi, a VPN. It only matters while Jira is
        /// known to be unreachable: then the retry is brought forward instead of waiting out
        /// the back-off. A connected session has nothing to redo, since every call carries its
        /// own credentials, and a refused one is not going to change with the network.
        /// </summary>
        private void OnNetworkChanged()
        {
            if (connecting)
            {
                networkChangedWhileConnecting = true;
                return;
            }

            if (!reconnector.IsEnabled)
                return;

            reconnector.Stop();
            reconnector.Interval = NetworkSettleDelay;
            reconnector.Start();
        }

        private void SetConnection(bool connected, string status)
        {
            IsConnected = connected;
            ConnectionStatus = status;
            ((RelayCommand)RefreshCommand).Refresh();
        }

        private void RefreshFromJira()
        {
            LoadAssignedParentIssues();

            // A refresh is an explicit "try again", so previously unresolved keys get another go
            catalog.ForgetUnresolved();
            ResolveMissingSummaries();
        }

        private void LoadAssignedParentIssues()
        {
            if (!session.SessionValid)
                return;

            Task.Factory.StartNew(() =>
            {
                string jql = "assignee = currentUser() AND issuetype not in subTaskIssueTypes() AND status != CLOSED ORDER BY key ASC";
                SearchResult result = session.Client.GetIssuesByJQL(jql);
                if (result == null || result.Issues == null)
                    return;

                OnUiThread(() =>
                {
                    parentIssues.Clear();

                    foreach (Issue issue in result.Issues)
                    {
                        string summary = issue.Fields == null ? "" : (issue.Fields.Summary ?? "");
                        parentIssues.Add(new IssueItem(issue.Key, summary));
                        RememberParentSummary(issue.Key, summary);
                    }

                    // This query only ever returns what is assigned to us, so anything the
                    // pickers found in Jira would drop off the list on every refresh.
                    foreach (IssueItem found in foundIssues)
                        if (!ContainsKey(parentIssues, found.Key))
                            parentIssues.Add(found);
                });
            });
        }

        /// <summary>
        /// Searches Jira for what was typed into a picker and adds the hits to
        /// <see cref="ParentIssues"/>. Runs off the UI thread and calls back on it, always —
        /// the combo has a search waiting on the answer, found or not.
        /// </summary>
        private void LookupIssues(string text, Action onDone)
        {
            string query = (text ?? "").Trim();

            if (query.Length < MinSearchLength || !session.SessionValid)
            {
                onDone();
                return;
            }

            string[] terms = IssueItem.Terms(query);

            Task.Factory.StartNew(() =>
            {
                List<IssueItem> found = catalog.Search(query);

                OnUiThread(() =>
                {
                    foreach (IssueItem item in found)
                    {
                        // Jira's picker answers on titles too, and a search by key means
                        // by key: an issue whose title happens to hold the digits typed is
                        // not what was asked for, and joining the list would be noise.
                        if (!item.Matches(terms) || ContainsKey(parentIssues, item.Key))
                            continue;

                        parentIssues.Add(item);
                        foundIssues.Add(item);
                        RememberParentSummary(item.Key, item.Summary);
                    }

                    onDone();
                });
            });
        }

        private static bool ContainsKey(ObservableCollection<IssueItem> issues, string key)
        {
            foreach (IssueItem item in issues)
                if (item.Key == key)
                    return true;

            return false;
        }

        /// <summary>Fills in the parent titles that persistence does not carry.</summary>
        private void ResolveMissingSummaries()
        {
            foreach (TimeEntryViewModel entry in Entries)
            {
                if (string.IsNullOrEmpty(entry.ParentKey) || !string.IsNullOrEmpty(entry.ParentSummary))
                    continue;

                string summary = catalog.Lookup(entry.ParentKey);
                if (!string.IsNullOrEmpty(summary))
                {
                    entry.ParentSummary = summary;
                    continue;
                }

                RequestIssueSummary(entry.ParentKey);
            }
        }

        private void RequestIssueSummary(string key)
        {
            if (!session.SessionValid || string.IsNullOrEmpty(key))
                return;

            if (!catalog.ShouldRequest(key))
                return;

            catalog.MarkPending(key);

            Task.Factory.StartNew(() =>
            {
                string summary = catalog.FetchSummary(key);

                OnUiThread(() =>
                {
                    if (string.IsNullOrEmpty(summary))
                    {
                        // Remember the failure so a redraw does not keep hitting Jira for this key
                        catalog.MarkUnresolved(key);
                        return;
                    }

                    catalog.ClearPending(key);
                    RememberParentSummary(key, summary);

                    foreach (TimeEntryViewModel entry in Entries)
                        if (entry.ParentKey == key)
                            entry.ParentSummary = summary;
                });
            });
        }

        private void RememberParentSummary(string key, string summary)
        {
            catalog.Remember(key, summary);
        }

        #endregion

        #region settings

        private void EditSettings()
        {
            SettingsChoice choice = dialogs.EditSettings(settings);
            if (!choice.Accepted)
                return;

            session.BaseUrl = settings.JiraBaseUrl;
            Logger.Instance.Enabled = settings.LoggingEnabled;

            if (choice.ImportedRows.Count > 0)
                AddImportedRows(choice.ImportedRows);

            // Switching between the four WPF layouts is live.
            ViewMode = ToViewMode(settings.StartupForm);

            if (IsJiraEnabled)
            {
                AuthenticateJira();
            }
            else
            {
                // Nothing to connect to any more: no retry, and no late answer overriding this
                CancelConnect();
                SetConnection(false, "Not configured");
            }

            EventHandler applied = SettingsApplied;
            if (applied != null)
                applied(this, EventArgs.Empty);
        }

        /// <summary>
        /// Appends rows recovered from a legacy install to the list that is already open —
        /// no restart needed. Always added paused, matching what LegacyImport already
        /// enforced; this is the same rebuild LoadPersistedRows does at startup, just onto a
        /// running Entries collection instead of an empty one.
        /// </summary>
        private void AddImportedRows(List<GridPersistedRow> rows)
        {
            foreach (GridPersistedRow row in rows)
            {
                string parentKey = (row.ParentIssue ?? "").Trim();
                TimeEntryViewModel entry = new TimeEntryViewModel(parentKey, "", "", "");

                if (row.TotalTime.TotalSeconds > 0)
                {
                    entry.Timer.SetState(new TimerState
                    {
                        Running = false,
                        SessionStartTime = row.SessionStartTime,
                        InitialStartTime = row.InitialStartTime,
                        TotalTime = row.TotalTime
                    });
                }

                AddEntry(entry);
            }

            ResolveMissingSummaries();
        }

        #endregion

        #region session lock

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
                OnUiThread(HandleSessionLock);
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
                OnUiThread(HandleSessionUnlock);
        }

        /// <summary>
        /// Pauses every running row — with "Allow multiple timers" there can be several — and
        /// remembers which, for the unlock to resume.
        /// </summary>
        internal void HandleSessionLock()
        {
            if (settings.PauseOnSessionLock == PauseAndResumeSetting.NoPause)
                return;

            // Added to, never replaced: a second lock without an unlock in between must not
            // forget the rows the first one paused.
            foreach (TimeEntryViewModel entry in Entries)
            {
                if (!entry.IsRunning)
                    continue;

                entry.Timer.Pause();
                entry.NotifyTimerChanged();

                if (!pausedOnLock.Contains(entry))
                    pausedOnLock.Add(entry);
            }

            UpdateRunningEntry();
            AfterTotalsChanged();
        }

        /// <summary>Resumes exactly the rows the lock paused, when the setting says so.</summary>
        internal void HandleSessionUnlock()
        {
            List<TimeEntryViewModel> paused = new List<TimeEntryViewModel>(pausedOnLock);
            pausedOnLock.Clear();

            if (settings.PauseOnSessionLock != PauseAndResumeSetting.PauseAndResume)
                return;

            foreach (TimeEntryViewModel entry in paused)
            {
                // Removed, or already going again: not the lock's to resume any more
                if (!Entries.Contains(entry) || entry.IsRunning)
                    continue;

                entry.Timer.Start();
                entry.NotifyTimerChanged();
            }

            UpdateRunningEntry();
            AfterTotalsChanged();
        }

        #endregion

        #region persistence

        private void LoadPersistedRows()
        {
            if (settings.GridPersistedRows == null || settings.GridPersistedRows.Count == 0)
                return;

            try
            {
                foreach (GridPersistedRow persistedRow in settings.GridPersistedRows)
                    AddEntry(LedgerPersistence.ToEntry(persistedRow, settings.SaveTimerState, LookupParentSummary));

                // Saved in order, so this is a no-op on a file this application wrote — it is
                // here for one edited by hand, or coming from a version that had no pins.
                SortPinnedFirst();
                Reindex();

                // A restored row may have come back running
                UpdateRunningEntry();
                RebuildQueue();
                ResolveMissingSummaries();

                Logger.Instance.Log(string.Format("Successfully loaded {0} persisted rows", settings.GridPersistedRows.Count));
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Error loading persisted rows: {0}", ex.Message));
                dialogs.Warn(string.Format("Error loading saved data:{0}{0}{1}", Environment.NewLine, ex.Message),
                    "Load Error");
            }
        }

        /// <summary>
        /// Writes the rows to settings.json. <paramref name="silent"/> is for the writes nobody
        /// asked for: a failure goes to the log only, since a dialog every minute — or over a
        /// crash report — would be worse than a missed write the next one retries.
        /// </summary>
        private void SavePersistedRows(bool silent)
        {
            try
            {
                settings.GridPersistedRows.Clear();

                foreach (TimeEntryViewModel entry in Entries)
                {
                    GridPersistedRow row = LedgerPersistence.ToRow(entry);
                    if (row != null)
                        settings.GridPersistedRows.Add(row);
                }

                settings.Save();

                // Once a minute would bury everything else in the log
                if (!silent)
                    Logger.Instance.Log(string.Format("Successfully saved {0} rows", settings.GridPersistedRows.Count));
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Error saving rows: {0}", ex.Message));

                if (!silent)
                    dialogs.Warn(string.Format("Error saving data:{0}{0}{1}", Environment.NewLine, ex.Message),
                        "Save Error");
            }
        }

        private void OnAutosaveTick(object sender, EventArgs e)
        {
            SaveRowsQuietly();
        }

        /// <summary>
        /// Something on the UI thread threw and nothing caught it. It is left unhandled —
        /// Program still reports it and the application still ends — but the window will never
        /// get to close, so this is the last chance to write what the timers measured since the
        /// last autosave. It runs before Program's error dialog, so the rows are safe whatever
        /// the user does with that.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            SaveRowsQuietly();
        }

        /// <summary>The autosave and the crash save. Neither may throw — see the catch.</summary>
        private void SaveRowsQuietly()
        {
            try
            {
                SavePersistedRows(true);
            }
            catch (Exception)
            {
                // Only the logging of a failed write can land here: on a full disk it throws
                // too. This save exists so that a crash loses nothing — it must not cause one,
                // nor, from the crash handler, replace the exception Program is about to report.
            }
        }

        #endregion

        #region helpers

        private void RaiseFocusRequested()
        {
            EventHandler handler = FocusRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void OnUiThread(Action action)
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }

        #endregion
    }
}
