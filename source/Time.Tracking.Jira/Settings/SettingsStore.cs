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
using System.IO;
using System.Text.Json;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// La configuracion tal como se guarda en disco. Los valores por defecto son los que traia
    /// el App.config de las versiones anteriores, para que una instalacion nueva arranque igual
    /// que antes.
    /// </summary>
    internal sealed class SettingsData
    {
        public string JiraBaseUrl { get; set; } = "http://myjiraserver.local/";
        public bool AlwaysOnTop { get; set; }
        public bool MinimizeToTray { get; set; }
        public int IssueCount { get; set; } = 6;
        public bool AllowMultipleTimers { get; set; }
        public bool IncludeProjectName { get; set; }

        public SaveTimerSetting SaveTimerState { get; set; }
        public PauseAndResumeSetting PauseOnSessionLock { get; set; }
        public WorklogCommentSetting PostWorklogComment { get; set; }
        public StartupFormSetting StartupForm { get; set; } = StartupFormSetting.Ledger;

        public string Username { get; set; } = "";

        /// <summary>
        /// Cifrada con DPAPI y en base64, igual que antes: se guarda tal cual se leyo, sin
        /// pasar nunca por disco en claro. Solo la descifra el mismo usuario de Windows.
        /// </summary>
        public string Password { get; set; } = "";

        public bool FirstRun { get; set; } = true;
        public int CurrentFilter { get; set; }
        public string StartTransitions { get; set; } = "";
        public bool LoggingEnabled { get; set; }
        public string ViewWindowSizes { get; set; } = "";

        /// <summary>
        /// Corner the mini window was left at. Both zeroes means it was never opened, and it
        /// docks top-right of the work area.
        /// </summary>
        public double MiniLeft { get; set; }
        public double MiniTop { get; set; }

        public List<GridPersistedRow> GridPersistedRows { get; set; } = new List<GridPersistedRow>();
    }


    /// <summary>
    /// Guarda y lee la configuracion en un JSON propio.
    /// </summary>
    /// <remarks>
    /// Hasta la 4.0.x la configuracion vivia en el user.config que administra
    /// System.Configuration, en una carpeta cuyo nombre calculaba .NET a partir del ensamblado
    /// y su version: %LOCALAPPDATA%\Seabury_Solutions\Sea.StopWatch.exe_Url_&lt;hash&gt;\&lt;version&gt;.
    /// Ese nombre cambiaba solo al cambiar la version, y encima depende de que el ejecutable
    /// gestionado se llame Sea.StopWatch.exe, cosa que dejo de ser cierta al migrar a .NET
    /// (ahora el exe es un apphost nativo y el ensamblado es Sea.StopWatch.dll).
    ///
    /// Por eso la configuracion pasa a un archivo en una ruta fija, que no depende ni de la
    /// version ni del runtime. <see cref="UserConfigImporter"/> trae por unica vez lo que haya
    /// dejado la instalacion anterior. <see cref="LegacyFilePath"/> hace lo mismo para la
    /// carpeta con el nombre de producto anterior a este mismo renombre.
    /// </remarks>
    internal static class SettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>
        /// %LOCALAPPDATA%\Seabury Solutions\Time.Tracking.Jira
        /// </summary>
        /// <remarks>
        /// Local y no Roaming: la contrasena esta cifrada con DPAPI de usuario, que no viaja
        /// bien entre maquinas, y los tiempos sin registrar son de la maquina donde se
        /// midieron.
        /// </remarks>
        internal static string DirectoryPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Seabury Solutions",
                    "Time.Tracking.Jira");
            }
        }

        internal static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "settings.json"); }
        }

        internal static bool Exists
        {
            get { return File.Exists(FilePath); }
        }

        /// <summary>
        /// %LOCALAPPDATA%\Seabury Solutions\Sea.StopWatch\settings.json — donde vivia la
        /// configuracion antes de renombrar la aplicacion a Time.Tracking.Jira.
        /// </summary>
        /// <remarks>
        /// Solo importa en el primer arranque con el nombre nuevo: si <see cref="FilePath"/> ya
        /// existe, esta ruta no se vuelve a mirar. La carpeta vieja se deja donde estaba, igual
        /// que <see cref="UserConfigImporter"/> hace con el user.config de las versiones sobre
        /// .NET Framework.
        /// </remarks>
        internal static string LegacyFilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Seabury Solutions",
                    "Sea.StopWatch",
                    "settings.json");
            }
        }

        internal static bool LegacyExists
        {
            get { return File.Exists(LegacyFilePath); }
        }

        /// <param name="setAside">La copia de un archivo que no se pudo leer; null si no hizo falta.</param>
        internal static SettingsData Load(out string setAside)
        {
            return LoadFrom(FilePath, out setAside);
        }

        internal static void Save(SettingsData data)
        {
            SaveTo(FilePath, data);
        }

        internal static SettingsData LoadFrom(string path)
        {
            string setAside;
            return LoadFrom(path, out setAside);
        }

        /// <summary>
        /// Lee el archivo. Si no existe o no se puede leer devuelve los valores por defecto,
        /// nunca falla: quedarse sin configuracion es molesto, no arrancar es peor.
        /// </summary>
        /// <remarks>
        /// Un archivo que existe pero no se puede leer se copia aparte antes de seguir
        /// (<paramref name="setAside"/>): el guardado automatico escribe los valores por defecto
        /// encima en menos de un minuto, y con el archivo se irian la conexion y el tiempo
        /// todavia sin registrar. Se copia y no se mueve: sin settings.json, el proximo arranque
        /// volveria a importar el user.config de la version anterior, con filas que puede que
        /// ya esten registradas en Jira.
        /// </remarks>
        internal static SettingsData LoadFrom(string path, out string setAside)
        {
            setAside = null;

            try
            {
                if (!File.Exists(path))
                    return new SettingsData();

                SettingsData data = JsonSerializer.Deserialize<SettingsData>(
                    File.ReadAllText(path), JsonOptions);

                if (data == null)
                    return new SettingsData();

                // Un archivo escrito a mano puede dejar la lista en null
                if (data.GridPersistedRows == null)
                    data.GridPersistedRows = new List<GridPersistedRow>();

                return data;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Could not read {0}: {1}", path, ex.Message));
                setAside = SetAside(path);
                return new SettingsData();
            }
        }

        /// <summary>
        /// Copia el archivo junto al original como settings.json.corrupt-aaaaMMdd-HHmmss.
        /// Devuelve la ruta de la copia, o null si no habia archivo o no se pudo copiar.
        /// </summary>
        private static string SetAside(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                string copy = string.Format("{0}.corrupt-{1:yyyyMMdd-HHmmss}", path, DateTime.Now);
                File.Copy(path, copy, true);

                Logger.Instance.Log(string.Format("Unreadable settings kept as {0}", copy));
                return copy;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Could not keep a copy of {0}: {1}", path, ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Escribe el archivo de forma atomica: primero un temporal y despues el reemplazo, para
        /// que un cierre a destiempo no deje un settings.json truncado.
        /// </summary>
        internal static void SaveTo(string path, SettingsData data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
                File.Move(temp, path, true);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Could not write {0}: {1}", path, ex.Message));
            }
        }
    }
}
