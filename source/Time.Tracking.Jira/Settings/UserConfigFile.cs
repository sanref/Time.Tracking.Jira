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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Lectura de los user.config que escribia System.Configuration en las versiones sobre
    /// .NET Framework. Lo usan los dos caminos de migracion que quedan: el de esta misma
    /// aplicacion (<see cref="UserConfigImporter"/>) y el del "Jira StopWatch" original
    /// (<see cref="LegacyImport"/>).
    /// </summary>
    /// <remarks>
    /// Se lee el XML directamente en vez de usar ConfigurationManager porque la gracia es
    /// justamente no depender de donde esa API cree que vive el archivo: aca la ruta la elige
    /// quien llama.
    /// </remarks>
    internal static class UserConfigFile
    {
        /// <summary>
        /// Los pares nombre/valor de la seccion de settings. Lanza si el XML esta mal formado;
        /// quien llama decide que hacer.
        /// </summary>
        internal static Dictionary<string, string> ReadValues(string path)
        {
            XDocument doc = XDocument.Load(path);

            return doc
                .Descendants("setting")
                .Where(e => e.Attribute("name") != null)
                .GroupBy(e => e.Attribute("name").Value)
                .ToDictionary(
                    g => g.Key,
                    g => (string)g.First().Element("value") ?? "");
        }

        /// <summary>
        /// El user.config escrito mas recientemente debajo de <paramref name="root"/>, o null.
        /// </summary>
        /// <remarks>
        /// Ordena por fecha de escritura y no por numero de version. Lo que conviene traerse es
        /// la configuracion de la version que el usuario uso por ultima vez, que no siempre es
        /// la de numero mas alto: una maquina que probo varias builds puede quedar con una
        /// carpeta de version mayor pero abandonada, y elegirla restauraria en silencio una
        /// configuracion vieja.
        ///
        /// Solo se miran las carpetas que .NET nombra con una version de ensamblado, que es la
        /// forma de descartar cualquier otra cosa que haya quedado en el arbol.
        /// </remarks>
        internal static string FindNewest(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return null;

            string newest = null;
            DateTime newestWrite = DateTime.MinValue;

            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(root, "user.config", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string candidate in candidates)
            {
                string versionDir = Path.GetFileName(Path.GetDirectoryName(candidate) ?? "");
                Version ignored;
                if (!Version.TryParse(versionDir, out ignored))
                    continue;

                DateTime written;
                try { written = File.GetLastWriteTimeUtc(candidate); }
                catch (Exception) { continue; }

                if (newest == null || written > newestWrite)
                {
                    newestWrite = written;
                    newest = candidate;
                }
            }

            return newest;
        }

        internal static string GetString(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? (value ?? "") : "";
        }

        internal static bool GetBool(Dictionary<string, string> values, string key)
        {
            bool result;
            return bool.TryParse(GetString(values, key), out result) && result;
        }

        internal static int GetInt(Dictionary<string, string> values, string key)
        {
            int result;
            return int.TryParse(GetString(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result : 0;
        }
    }
}
