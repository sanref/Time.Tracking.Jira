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

namespace Time.Tracking.Jira
{
    public enum SaveTimerSetting
    {
        NoSave,
        SavePause,
        SaveRunActive
    }

    public enum PauseAndResumeSetting
    {
        NoPause,
        Pause,
        PauseAndResume
    }

    public enum WorklogCommentSetting
    {
        WorklogOnly,
        CommentOnly,
        WorklogAndComment
    }

    /// <summary>
    /// Which WPF view opens on start. Persisted as its numeric value, so the existing entries
    /// keep theirs: 0 was the classic WinForms window, since removed — a config still holding
    /// it falls back to Ledger, same as 1 always meant.
    /// </summary>
    public enum StartupFormSetting
    {
        Ledger = 1,
        Cards = 2,
        Focus = 3,
        Grid = 4
    }

    internal sealed class Settings
    {
        public static readonly Settings Instance = new Settings();

        #region public members
        public string JiraBaseUrl { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool MinimizeToTray { get; set; }
        public int IssueCount { get; set; }
        public bool AllowMultipleTimers { get; set; }
        public bool IncludeProjectName { get; set; }

        public SaveTimerSetting SaveTimerState { get; set; }
        public PauseAndResumeSetting PauseOnSessionLock { get; set; }
        public WorklogCommentSetting PostWorklogComment { get; set; }

        public StartupFormSetting StartupForm { get; set; }

        public string Username { get; set; }
        public string Password { get; set; }
        public bool FirstRun { get; set; }

        public int CurrentFilter { get; set; }

        public List<GridPersistedRow> GridPersistedRows { get; private set; }

        public string StartTransitions { get; set; }

        public bool LoggingEnabled { get; set; }

        /// <summary>
        /// Window size per v4 view, as "Ledger=900x600;Grid=920x560". Each layout keeps its
        /// own, because their natural sizes differ too much to share one.
        /// </summary>
        public string ViewWindowSizes { get; set; }

        /// <summary>
        /// Where the mini window was last dragged to, so it opens back in the same corner.
        /// Both zeroes means it was never opened. See <see cref="Wpf.Views.MiniWindow"/>.
        /// </summary>
        public double MiniLeft { get; set; }
        public double MiniTop { get; set; }

        /// <summary>
        /// Archivo del que se trajo la configuracion en este arranque (el settings.json de la
        /// carpeta con el nombre anterior, o un user.config de .NET Framework), o null si no
        /// hubo migracion. Lo consulta Program.cs para dejarlo en el log, que recien queda
        /// configurado despues de Load().
        /// </summary>
        public string ImportedFrom { get; private set; }

        /// <summary>
        /// Copia del settings.json que no se pudo leer en este arranque, o null si se leyo bien.
        /// La aplicacion arranca con los valores por defecto y avisa donde quedo el original.
        /// </summary>
        public string CorruptCopy { get; private set; }
        #endregion


        #region public methods
        /// <summary>
        /// Deja la instancia con la configuracion vigente. Devuelve false cuando no habia nada
        /// que leer y se arranca con los valores por defecto (instalacion nueva).
        /// </summary>
        /// <remarks>
        /// Primer arranque con el nombre nuevo: todavia no existe settings.json bajo
        /// Time.Tracking.Jira, asi que primero se busca el que dejo la carpeta Sea.StopWatch de
        /// antes del renombre (ver <see cref="SettingsStore.LegacyFilePath"/>). Solo si tampoco
        /// hay eso se cae al user.config de una version sobre .NET Framework, mas vieja todavia
        /// — ver <see cref="UserConfigImporter"/> para por que hace falta.
        /// </remarks>
        public bool Load()
        {
            ImportedFrom = null;
            CorruptCopy = null;

            if (!SettingsStore.Exists)
            {
                if (SettingsStore.LegacyExists)
                {
                    string corruptCopyFromRename;
                    SettingsData renamed = SettingsStore.LoadFrom(SettingsStore.LegacyFilePath, out corruptCopyFromRename);

                    SettingsStore.Save(renamed);
                    ImportedFrom = SettingsStore.LegacyFilePath;
                    CorruptCopy = corruptCopyFromRename;
                    ApplyFrom(renamed);
                    return true;
                }

                string source;
                SettingsData imported = UserConfigImporter.Import(out source);

                if (imported == null)
                {
                    ApplyFrom(new SettingsData());
                    return false;
                }

                SettingsStore.Save(imported);
                ImportedFrom = source;
                ApplyFrom(imported);
                return true;
            }

            string corruptCopy;
            ApplyFrom(SettingsStore.Load(out corruptCopy));
            CorruptCopy = corruptCopy;
            return true;
        }


        public void Save()
        {
            lock (_writeLock)
            {
                SettingsStore.Save(ToData());
            }
        }
        #endregion


        #region private methods
        private void ApplyFrom(SettingsData data)
        {
            this.JiraBaseUrl = data.JiraBaseUrl ?? "";

            this.AlwaysOnTop = data.AlwaysOnTop;
            this.IncludeProjectName = data.IncludeProjectName;
            this.MinimizeToTray = data.MinimizeToTray;
            this.IssueCount = data.IssueCount;
            this.Username = data.Username;

            this.Password = DecryptPassword(data.Password);

            this.FirstRun = data.FirstRun;
            this.SaveTimerState = data.SaveTimerState;
            this.PauseOnSessionLock = data.PauseOnSessionLock;
            this.PostWorklogComment = data.PostWorklogComment;

            this.StartupForm = data.StartupForm;

            this.CurrentFilter = data.CurrentFilter;

            this.GridPersistedRows = data.GridPersistedRows ?? new List<GridPersistedRow>();

            this.AllowMultipleTimers = data.AllowMultipleTimers;

            this.StartTransitions = data.StartTransitions;

            this.LoggingEnabled = data.LoggingEnabled;

            this.ViewWindowSizes = data.ViewWindowSizes ?? "";

            this.MiniLeft = data.MiniLeft;
            this.MiniTop = data.MiniTop;
        }


        private SettingsData ToData()
        {
            return new SettingsData
            {
                JiraBaseUrl = this.JiraBaseUrl,

                AlwaysOnTop = this.AlwaysOnTop,
                MinimizeToTray = this.MinimizeToTray,
                IssueCount = this.IssueCount,
                IncludeProjectName = this.IncludeProjectName,

                Username = this.Username,
                Password = EncryptPassword(this.Password),

                FirstRun = this.FirstRun,
                SaveTimerState = this.SaveTimerState,
                PauseOnSessionLock = this.PauseOnSessionLock,
                PostWorklogComment = this.PostWorklogComment,

                StartupForm = this.StartupForm,

                CurrentFilter = this.CurrentFilter,

                GridPersistedRows = this.GridPersistedRows ?? new List<GridPersistedRow>(),

                AllowMultipleTimers = this.AllowMultipleTimers,

                StartTransitions = this.StartTransitions,

                LoggingEnabled = this.LoggingEnabled,

                ViewWindowSizes = this.ViewWindowSizes ?? "",

                MiniLeft = this.MiniLeft,
                MiniTop = this.MiniTop
            };
        }


        /// <summary>
        /// La contrasena se guarda cifrada con DPAPI del usuario actual. Un blob que no se puede
        /// descifrar (perfil distinto, configuracion copiada a mano de otra maquina) se trata
        /// como "sin contrasena": la aplicacion la vuelve a pedir, que es mejor que no abrir.
        /// </summary>
        private static string DecryptPassword(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted))
                return "";

            try { return DPAPI.Decrypt(encrypted); }
            catch (Exception) { return ""; }
        }


        private static string EncryptPassword(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "";

            return DPAPI.Encrypt(plainText);
        }


        private Settings()
        {
            this.GridPersistedRows = new List<GridPersistedRow>();
        }
        #endregion


        private Object _writeLock = new Object();
    }
}
