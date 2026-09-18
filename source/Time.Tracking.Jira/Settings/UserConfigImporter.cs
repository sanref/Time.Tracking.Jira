/**************************************************************************
Copyright 2026 fnas (based in Jira StopWatch by Carsten Gehling)

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
using Time.Tracking.Jira.Logging;
using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Trae, una sola vez, la configuracion que dejo una instalacion anterior de esta misma
    /// aplicacion sobre .NET Framework (3.x o 4.0.x).
    /// </summary>
    /// <remarks>
    /// Hasta la 4.0.x la configuracion la manejaba System.Configuration y vivia en
    ///     %LOCALAPPDATA%\Seabury_Solutions\Sea.StopWatch.exe_Url_&lt;hash&gt;\&lt;version&gt;\user.config
    /// donde el hash sale de la ruta del ejecutable y &lt;version&gt; es el AssemblyVersion. Al pasar
    /// a .NET ese nombre ya no se puede reproducir: el ensamblado gestionado paso a llamarse
    /// Sea.StopWatch.dll, asi que cambian tanto el nombre de la carpeta como el hash. Sin este
    /// paso el usuario abriria la 4.1 sin conexion a Jira, sin credenciales y sin los tiempos
    /// que todavia no registro.
    ///
    /// El user.config de origen se lee y no se toca. Queda donde estaba a proposito: es lo que
    /// permite volver a la version anterior si hiciera falta.
    /// </remarks>
    internal static class UserConfigImporter
    {
        /// <summary>
        /// %LOCALAPPDATA%\Seabury_Solutions
        /// </summary>
        /// <remarks>
        /// Con guion bajo: .NET saneaba el AssemblyCompany ("Seabury Solutions") al armar la
        /// ruta. La carpeta nueva si usa el nombre con espacio, ver <see cref="SettingsStore"/>.
        /// </remarks>
        internal static string LegacyRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Seabury_Solutions");
            }
        }

        /// <summary>
        /// Lee la configuracion anterior, o devuelve null si no hay ninguna que traer.
        /// </summary>
        /// <param name="source">Ruta del user.config usado, para dejar registro.</param>
        internal static SettingsData Import(out string source)
        {
            source = null;

            try
            {
                source = UserConfigFile.FindNewest(LegacyRoot);
                if (source == null)
                    return null;

                return Read(source);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format(
                    "Could not import the settings of a previous version: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Convierte un user.config concreto. Los nombres de setting son los mismos que usaba
        /// la aplicacion, asi que el mapeo es directo.
        /// </summary>
        internal static SettingsData Read(string userConfigPath)
        {
            Dictionary<string, string> values = UserConfigFile.ReadValues(userConfigPath);

            SettingsData data = new SettingsData
            {
                JiraBaseUrl = UserConfigFile.GetString(values, "JiraBaseUrl"),
                AlwaysOnTop = UserConfigFile.GetBool(values, "AlwaysOnTop"),
                MinimizeToTray = UserConfigFile.GetBool(values, "MinimizeToTray"),
                IssueCount = UserConfigFile.GetInt(values, "IssueCount"),
                AllowMultipleTimers = UserConfigFile.GetBool(values, "AllowMultipleTimers"),
                IncludeProjectName = UserConfigFile.GetBool(values, "IncludeProjectName"),

                SaveTimerState = (SaveTimerSetting)UserConfigFile.GetInt(values, "SaveTimerState"),
                PauseOnSessionLock = (PauseAndResumeSetting)UserConfigFile.GetInt(values, "PauseOnSessionLock"),
                PostWorklogComment = (WorklogCommentSetting)UserConfigFile.GetInt(values, "PostWorklogComment"),
                StartupForm = (StartupFormSetting)UserConfigFile.GetInt(values, "StartupForm"),

                Username = UserConfigFile.GetString(values, "Username"),
                // Se copia tal cual, cifrada: DPAPI la descifra igual porque es el mismo
                // usuario de Windows, y asi la contrasena no pasa en claro por la migracion.
                Password = UserConfigFile.GetString(values, "Password"),

                FirstRun = UserConfigFile.GetBool(values, "FirstRun"),
                CurrentFilter = UserConfigFile.GetInt(values, "CurrentFilter"),
                StartTransitions = UserConfigFile.GetString(values, "StartTransitions"),
                LoggingEnabled = UserConfigFile.GetBool(values, "LoggingEnabled"),
                ViewWindowSizes = UserConfigFile.GetString(values, "ViewWindowSizes"),

                GridPersistedRows = ReadGridRows(UserConfigFile.GetString(values, "GridPersistedRows"))
            };

            // IssueCount a 0 dejaria la grilla sin filas: un user.config sin el valor (3.x muy
            // viejo) vuelve al mismo default que traia el App.config.
            if (data.IssueCount <= 0)
                data.IssueCount = 6;

            return data;
        }

        /// <summary>
        /// Las filas de la grilla, que se guardaban como un List&lt;GridPersistedRow&gt; en base64
        /// escrito con BinaryFormatter.
        /// </summary>
        private static List<GridPersistedRow> ReadGridRows(string base64)
        {
            List<GridPersistedRow> rows = new List<GridPersistedRow>();

            foreach (ClassRecord record in NrbfReader.ReadListItems(base64))
            {
                rows.Add(new GridPersistedRow
                {
                    ParentIssue = NrbfReader.GetString(record, "ParentIssue"),
                    Subtask = NrbfReader.GetString(record, "Subtask"),
                    TimerRunning = NrbfReader.GetBoolean(record, "TimerRunning"),
                    InitialStartTime = NrbfReader.GetDateTimeOffset(record, "InitialStartTime"),
                    SessionStartTime = NrbfReader.GetDateTime(record, "SessionStartTime"),
                    TotalTime = NrbfReader.GetTimeSpan(record, "TotalTime")
                });
            }

            return rows;
        }
    }
}
