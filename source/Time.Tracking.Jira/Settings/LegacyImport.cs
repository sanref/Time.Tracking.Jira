/**************************************************************************
Copyright 2016 Carsten Gehling

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
**************************************************************************/
using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Reads the settings of the original "Jira StopWatch" this app was forked from — same
    /// <c>StopWatch.Properties.Settings</c> schema we still use today, minus <c>StartupForm</c>,
    /// which did not exist yet. Its user.config does not live next to the install folder
    /// (typically %ProgramFiles(x86)%\Carsten Gehling\Jira StopWatch): like ours, .NET keeps
    /// per-user settings under %LOCALAPPDATA%. That app's AssemblyCompany was blank, so the
    /// path collapses to a single "StopWatch" folder instead of the Company\Product nesting
    /// ours has.
    /// </summary>
    internal static class LegacyImport
    {
        /// <summary>Everything recovered from one legacy user.config.</summary>
        internal class Result
        {
            public string SourcePath;
            public string SourceVersion;

            public string BaseUrl;
            public string Username;
            public string ApiToken;
            public bool TokenDecryptFailed;

            public bool AlwaysOnTop;
            public bool MinimizeToTray;
            public bool IncludeProjectName;
            public bool AllowMultipleTimers;
            public bool DebugLogging;
            public int IssueCount;
            public int CurrentFilter;
            public string StartTransitions;

            public SaveTimerSetting OnExit;
            public PauseAndResumeSetting OnSessionLock;
            public WorklogCommentSetting CommentMode;

            /// <summary>Recovered issues with unlogged time, converted for the Ledger.
            /// Empty rows (no key, no time) are dropped — nothing worth recovering.</summary>
            public List<GridPersistedRow> Rows = new List<GridPersistedRow>();
        }

        /// <summary>
        /// Newest user.config under %LOCALAPPDATA%\StopWatch, by write time — same reasoning
        /// as our own cross-version migration: the highest version number is not necessarily
        /// the one the user actually used last. Null when nothing is found.
        /// </summary>
        internal static string FindDefaultUserConfig()
        {
            return UserConfigFile.FindNewest(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StopWatch"));
        }

        /// <summary>Parses one user.config. Throws on malformed XML; callers should catch.</summary>
        internal static Result Load(string userConfigPath)
        {
            Dictionary<string, string> values = UserConfigFile.ReadValues(userConfigPath);

            Result result = new Result
            {
                SourcePath = userConfigPath,
                SourceVersion = VersionFromPath(userConfigPath),

                BaseUrl = UserConfigFile.GetString(values, "JiraBaseUrl"),
                Username = UserConfigFile.GetString(values, "Username"),
                AlwaysOnTop = UserConfigFile.GetBool(values, "AlwaysOnTop"),
                MinimizeToTray = UserConfigFile.GetBool(values, "MinimizeToTray"),
                IncludeProjectName = UserConfigFile.GetBool(values, "IncludeProjectName"),
                AllowMultipleTimers = UserConfigFile.GetBool(values, "AllowMultipleTimers"),
                DebugLogging = UserConfigFile.GetBool(values, "LoggingEnabled"),
                IssueCount = UserConfigFile.GetInt(values, "IssueCount"),
                CurrentFilter = UserConfigFile.GetInt(values, "CurrentFilter"),
                StartTransitions = UserConfigFile.GetString(values, "StartTransitions"),

                OnExit = (SaveTimerSetting)UserConfigFile.GetInt(values, "SaveTimerState"),
                OnSessionLock = (PauseAndResumeSetting)UserConfigFile.GetInt(values, "PauseOnSessionLock"),
                CommentMode = (WorklogCommentSetting)UserConfigFile.GetInt(values, "PostWorklogComment")
            };

            string encryptedToken = UserConfigFile.GetString(values, "ApiToken");
            if (!string.IsNullOrEmpty(encryptedToken))
            {
                try { result.ApiToken = DPAPI.Decrypt(encryptedToken); }
                catch { result.TokenDecryptFailed = true; }
            }

            string persistedIssues = UserConfigFile.GetString(values, "PersistedIssues");
            if (!string.IsNullOrEmpty(persistedIssues))
                result.Rows = ReadLegacyIssues(persistedIssues);

            return result;
        }

        /// <summary>The version folder name, e.g. "...\2.3.0.0\user.config" -> "2.3.0.0".</summary>
        private static string VersionFromPath(string userConfigPath)
        {
            string versionDir = Path.GetFileName(Path.GetDirectoryName(userConfigPath) ?? "");
            return string.IsNullOrEmpty(versionDir) ? "?" : versionDir;
        }

        /// <summary>
        /// Reads the legacy List&lt;PersistedIssue&gt; blob and converts each row into a
        /// GridPersistedRow the Ledger understands. The legacy app never split parent from
        /// subtask, so the whole key becomes the parent — an ordinary, already-supported shape
        /// (any row with no subtask behaves this way today).
        /// </summary>
        /// <remarks>
        /// El blob lo escribio BinaryFormatter, que ya no existe. <see cref="NrbfReader"/> lee
        /// ese formato sin construir ningun objeto: solo hacen falta los nombres de los campos,
        /// que nunca cambiaron. Antes hacia falta ademas un SerializationBinder para remapear el
        /// nombre del ensamblado (StopWatch -> Sea.StopWatch); ahora es innecesario, porque no
        /// se resuelve ningun tipo.
        /// </remarks>
        private static List<GridPersistedRow> ReadLegacyIssues(string base64)
        {
            List<GridPersistedRow> rows = new List<GridPersistedRow>();

            foreach (ClassRecord issue in NrbfReader.ReadListItems(base64))
            {
                string key = NrbfReader.GetString(issue, "Key").Trim();
                TimeSpan totalTime = NrbfReader.GetTimeSpan(issue, "TotalTime");

                if (string.IsNullOrEmpty(key) && totalTime.TotalSeconds <= 0)
                    continue; // one of MainForm's blank slots — nothing to recover

                rows.Add(new GridPersistedRow
                {
                    ParentIssue = key,
                    Subtask = "",
                    // Always comes back paused: silently resuming a timer the user hasn't
                    // looked at in months would tick up unlogged time behind their back.
                    TimerRunning = false,
                    SessionStartTime = NrbfReader.GetDateTime(issue, "SessionStartTime"),
                    InitialStartTime = NrbfReader.GetDateTimeOffset(issue, "InitialStartTime"),
                    TotalTime = totalTime
                });
            }

            return rows;
        }
    }
}
