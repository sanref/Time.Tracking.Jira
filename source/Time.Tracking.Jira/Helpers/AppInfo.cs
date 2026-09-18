using System;
using System.Diagnostics;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Name and version of the running application as shown in the UI.
    /// </summary>
    /// <remarks>
    /// The version comes from the file version, not from the assembly version: the latter is
    /// pinned per major release (see GitVersion.yml) and it would always read 4.0.0.0.
    ///
    /// Se lee del ejecutable y no del ensamblado porque desde .NET son dos archivos: el .exe es
    /// un apphost nativo, al que el SDK le copia los recursos de version, y el ensamblado
    /// gestionado es el .dll de al lado. Es ademas el mismo archivo del que el instalador toma
    /// su numero de version.
    /// </remarks>
    internal static class AppInfo
    {
        private static readonly FileVersionInfo versionInfo =
            FileVersionInfo.GetVersionInfo(Environment.ProcessPath);

        /// <summary>
        /// Version for the window title, e.g. "4.0.1". The patch is part of it: within a
        /// major the assembly version never moves, so this is the only number in the UI that
        /// tells one build from another.
        /// </summary>
        public static string ShortVersion
        {
            get
            {
                return string.Format("{0}.{1}.{2}",
                    versionInfo.FileMajorPart, versionInfo.FileMinorPart, versionInfo.FileBuildPart);
            }
        }

        /// <summary>Window title, e.g. "Time.Tracking.Jira 4.0.1".</summary>
        public static string Title
        {
            get { return string.Format("{0} {1}", versionInfo.ProductName, ShortVersion); }
        }
    }
}
