using Time.Tracking.Jira.Logging;
using Time.Tracking.Jira.Wpf.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Time.Tracking.Jira.Wpf.ViewModels
{
    /// <summary>
    /// Backs <see cref="Views.SettingsWindow"/>. Mirrors the fields the old WinForms
    /// SettingsForm read and wrote, down to the same enums, so <c>Settings</c> itself and
    /// everything that reads it afterwards (Program.cs, LedgerViewModel) needed no changes.
    /// </summary>
    public class SettingsViewModel : ObservableObject
    {
        #region connection

        private string baseUrl = "";
        public string BaseUrl { get { return baseUrl; } set { Set(ref baseUrl, value); } }

        private string username = "";
        public string Username { get { return username; } set { Set(ref username, value); } }

        // PasswordBox can't bind Password; SettingsWindow pushes it here on PasswordChanged.
        private string apiToken = "";
        public string ApiToken { get { return apiToken; } set { Set(ref apiToken, value); } }

        private bool debugLogging;
        public bool DebugLogging { get { return debugLogging; } set { Set(ref debugLogging, value); } }

        #endregion

        #region general options

        private bool alwaysOnTop;
        public bool AlwaysOnTop { get { return alwaysOnTop; } set { Set(ref alwaysOnTop, value); } }

        private bool minimizeToTray;
        public bool MinimizeToTray { get { return minimizeToTray; } set { Set(ref minimizeToTray, value); } }

        private bool allowMultipleTimers;
        public bool AllowMultipleTimers { get { return allowMultipleTimers; } set { Set(ref allowMultipleTimers, value); } }

        /// <summary>
        /// Mono on Mac/Linux never implemented NotifyIcon, so the tray checkbox is hidden
        /// there, same as the WinForms dialog did.
        /// </summary>
        public bool IsMinimizeToTraySupported
        {
            get { return CrossPlatformHelpers.IsWindowsEnvironment(); }
        }

        #endregion

        #region behaviour

        private StartupFormSetting startupForm = StartupFormSetting.Ledger;
        public StartupFormSetting StartupForm { get { return startupForm; } set { Set(ref startupForm, value); } }

        private SaveTimerSetting onExit = SaveTimerSetting.SavePause;
        public SaveTimerSetting OnExit { get { return onExit; } set { Set(ref onExit, value); } }

        private PauseAndResumeSetting onSessionLock = PauseAndResumeSetting.PauseAndResume;
        public PauseAndResumeSetting OnSessionLock { get { return onSessionLock; } set { Set(ref onSessionLock, value); } }

        // No control in this dialog binds these — they only exist so Load/Save round-trips
        // them like every other field, and so a legacy import has somewhere to land them
        // that Cancel still discards cleanly.
        //
        // The first three had a control until 4.1.3, and nothing ever read them: the comment
        // always went into the worklog body, the project name was never appended, and no
        // transition was ever performed. They keep round-tripping so a value set by an older
        // version — or by a legacy import — survives untouched, here and in settings.json.
        private bool includeProjectName;
        private WorklogCommentSetting commentMode = WorklogCommentSetting.WorklogOnly;
        private string playStateChanges = "";
        private int issueCount;
        private int currentFilter;

        #endregion

        #region commands

        public ICommand CreateTokenCommand { get; private set; }
        public ICommand OpenLogFolderCommand { get; private set; }
        public ICommand ImportLegacyCommand { get; private set; }

        public SettingsViewModel()
        {
            // Fixed Atlassian Cloud token page, exactly what the old dialog opened — not
            // derived from BaseUrl, which would point at a Server/Data Center PAT page instead.
            CreateTokenCommand = new RelayCommand(() => OpenUrl("https://id.atlassian.com/manage/api-tokens"));
            OpenLogFolderCommand = new RelayCommand(() => OpenUrl(LogFolderPath()));
            ImportLegacyCommand = new RelayCommand(ImportLegacy);
        }

        #endregion

        #region load / save

        /// <summary>Copies the live settings into the editable fields.</summary>
        internal void Load(Settings settings)
        {
            BaseUrl = settings.JiraBaseUrl;
            Username = settings.Username;
            ApiToken = settings.Password;
            DebugLogging = settings.LoggingEnabled;

            AlwaysOnTop = settings.AlwaysOnTop;
            MinimizeToTray = settings.MinimizeToTray;
            includeProjectName = settings.IncludeProjectName;
            AllowMultipleTimers = settings.AllowMultipleTimers;

            StartupForm = settings.StartupForm;
            OnExit = settings.SaveTimerState;
            OnSessionLock = settings.PauseOnSessionLock;
            commentMode = settings.PostWorklogComment;
            playStateChanges = settings.StartTransitions;

            issueCount = settings.IssueCount;
            currentFilter = settings.CurrentFilter;
        }

        /// <summary>Writes the fields back. Does not call settings.Save() — the caller decides when.</summary>
        internal void Save(Settings settings)
        {
            settings.JiraBaseUrl = BaseUrl;
            settings.Username = Username;
            settings.Password = ApiToken;
            settings.LoggingEnabled = DebugLogging;

            settings.AlwaysOnTop = AlwaysOnTop;
            settings.MinimizeToTray = MinimizeToTray;
            settings.IncludeProjectName = includeProjectName;
            settings.AllowMultipleTimers = AllowMultipleTimers;

            settings.StartupForm = StartupForm;
            settings.SaveTimerState = OnExit;
            settings.PauseOnSessionLock = OnSessionLock;
            settings.PostWorklogComment = commentMode;
            settings.StartTransitions = playStateChanges;

            settings.IssueCount = issueCount;
            settings.CurrentFilter = currentFilter;
        }

        #endregion

        #region legacy import

        // Populated by ImportLegacyCommand, consumed by LedgerViewModel after OK — added to
        // the live entry list there, not written straight into Settings, so Cancel discards
        // an import exactly like it discards every other field on this dialog.
        private List<GridPersistedRow> importedRows = new List<GridPersistedRow>();
        internal List<GridPersistedRow> ImportedRows { get { return importedRows; } }

        /// <summary>Source app version of the last successful import, for the caller's own summary.</summary>
        internal string ImportedFromVersion { get; private set; }

        private void ImportLegacy()
        {
            string path = LegacyImport.FindDefaultUserConfig();

            if (path == null)
            {
                using (System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog())
                {
                    dialog.Title = "Locate the old Jira StopWatch's user.config";
                    dialog.Filter = "Settings file (user.config)|user.config|All files (*.*)|*.*";

                    string legacyInstall = @"C:\Program Files (x86)\Carsten Gehling\Jira StopWatch";
                    dialog.InitialDirectory = Directory.Exists(legacyInstall)
                        ? legacyInstall
                        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return;

                    path = dialog.FileName;
                }
            }
            else
            {
                MessageBoxResult go = MessageBox.Show(
                    string.Format("Found a settings file from the old Jira StopWatch:\n{0}\n\n" +
                        "This will replace the connection details and options above with the ones found there, " +
                        "and add any unlogged issues to your current list. Continue?", path),
                    "Import from Jira StopWatch", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (go != MessageBoxResult.Yes)
                    return;
            }

            LegacyImport.Result result;
            try
            {
                result = LegacyImport.Load(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read that file:\n\n" + ex.Message,
                    "Import from Jira StopWatch", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BaseUrl = result.BaseUrl;
            Username = result.Username;
            if (!result.TokenDecryptFailed)
                ApiToken = result.ApiToken ?? "";
            DebugLogging = result.DebugLogging;
            AlwaysOnTop = result.AlwaysOnTop;
            MinimizeToTray = result.MinimizeToTray;
            includeProjectName = result.IncludeProjectName;
            AllowMultipleTimers = result.AllowMultipleTimers;
            OnExit = result.OnExit;
            OnSessionLock = result.OnSessionLock;
            commentMode = result.CommentMode;
            playStateChanges = result.StartTransitions;
            issueCount = result.IssueCount;
            currentFilter = result.CurrentFilter;

            importedRows = result.Rows;
            ImportedFromVersion = result.SourceVersion;

            TimeSpan totalRecovered = TimeSpan.Zero;
            foreach (GridPersistedRow row in result.Rows)
                totalRecovered += row.TotalTime;

            string summary = result.Rows.Count == 0
                ? "No unlogged issues were found."
                : string.Format("{0} unlogged issue{1} found, totaling {2}h {3:00}m. " +
                    "They will be added to your list once you click OK below.",
                    result.Rows.Count, result.Rows.Count == 1 ? "" : "s",
                    (int)totalRecovered.TotalHours, totalRecovered.Minutes);

            string tokenNote = result.TokenDecryptFailed
                ? "\n\nThe saved API token could not be decrypted — it was likely encrypted under a " +
                  "different Windows account — and was left as it was."
                : "";

            MessageBox.Show(
                string.Format("Imported settings from Jira StopWatch {0}.\n\n{1}{2}",
                    result.SourceVersion, summary, tokenNote),
                "Import from Jira StopWatch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        private static void OpenUrl(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return;

            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { /* nothing useful to show the user here */ }
        }

        /// <summary>
        /// Directory holding the logfile, or null if Logger.Instance.LogfilePath hasn't been
        /// set (e.g. Program.cs failed to wire it up) — never let Path.GetDirectoryName(null)
        /// throw out of a command handler.
        /// </summary>
        private static string LogFolderPath()
        {
            string logfilePath = Logger.Instance.LogfilePath;
            return string.IsNullOrEmpty(logfilePath) ? null : Path.GetDirectoryName(logfilePath);
        }
    }
}
