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
using Time.Tracking.Jira.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Time.Tracking.Jira
{
    static class Program
    {

        static Mutex mutex = new Mutex(true, "{D5597999-20FE-430F-8E5D-8893EBED2599}");

        // Junto al settings.json, en una carpeta que no depende de la version. Antes salia de
        // Application.UserAppDataPath, que incluye la version del producto y por lo tanto
        // estrenaba carpeta -y perdia el historial- en cada release.
        static string logPath = Path.Combine(SettingsStore.DirectoryPath, "timetrackingjira.log");

        [STAThread]
        static void Main()
        {
            if (mutex.WaitOne(TimeSpan.Zero, true))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += new ThreadExceptionEventHandler(Application_ThreadException);
                AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

                // Jira runs on background tasks. A task that faults without anyone reading its
                // result used to disappear in silence — the app kept going with, say, a worklog
                // that never posted and nothing written down anywhere.
                TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

                // Load settings so the WPF tracker knows which view to start in
                Settings settings = Settings.Instance;
                settings.Load();

                // En una instalacion nueva no hay nada guardado todavia, asi que la carpeta
                // puede no existir: el log y el manejador de errores escriben ahi.
                try { Directory.CreateDirectory(SettingsStore.DirectoryPath); }
                catch (Exception) { }

                // Logger.Instance is a singleton with no default logfile path; wire it up here,
                // once, so every Logger.Instance.Log() call anywhere in the app actually writes.
                Logger.Instance.LogfilePath = logPath;
                Logger.Instance.Enabled = settings.LoggingEnabled;

                // Recien ahora se puede registrar: la migracion ocurre dentro de Load(), antes
                // de saber si el usuario tiene el log activado.
                if (settings.ImportedFrom != null)
                    Logger.Instance.Log(string.Format("Settings imported from {0}", settings.ImportedFrom));

                if (settings.CorruptCopy != null)
                    Logger.Instance.Log(string.Format("settings.json could not be read; started with defaults, the original kept as {0}", settings.CorruptCopy));

                RunTracker();

                mutex.ReleaseMutex();
            }
            else {
                // Send Win32 message to make the currently running instance jump on top of all the other windows
                NativeMethods.PostMessage(
                    (IntPtr)NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SHOWME,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
        }


        /// <summary>
        /// Runs the v4 WPF tracker window on its own Application. The only UI this app has.
        /// </summary>
        static void RunTracker()
        {
            var app = new System.Windows.Application();
            app.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;

            // StaticResource is resolved while the window is being parsed, so the theme has
            // to be in Application.Resources before the window is constructed.
            app.Resources.MergedDictionaries.Add(LoadTheme("Tokens"));
            app.Resources.MergedDictionaries.Add(LoadTheme("Controls"));
            app.Resources.MergedDictionaries.Add(LoadTheme("Forms"));

            app.Run(new Time.Tracking.Jira.Wpf.TrackerWindow());
        }


        static System.Windows.ResourceDictionary LoadTheme(string name)
        {
            return new System.Windows.ResourceDictionary
            {
                Source = new Uri(string.Format("/Time.Tracking.Jira;component/Wpf/Theme/{0}.xaml", name), UriKind.Relative)
            };
        }


        #region system eventhandlers
        static void SystemEvents_SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
        }


        static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteLog("Unobserved Task Exception");
            WriteLog(e.Exception.ToString());

            // Observed: the process stays up. Nothing is shown either — by the time the
            // finalizer gets here the user has moved on, and whatever actually mattered
            // already reported itself through its own catch.
            e.SetObserved();
        }


        static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            WriteLog("Unhandled Thread Exception");
            WriteLog(e.Exception.ToString());

            DisplayErrorHandled();
        }


        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteLog("Unhandled UI Exception");

            // The CLR allows throwing a non-Exception object (rare, but legal), in which case
            // this cast comes back null — guard it so the crash handler itself can't NRE.
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                // ToString rather than Message + StackTrace: it carries the inner exceptions,
                // which is where the cause is when XAML wraps it — a window that fails to build
                // only says "Set property ... threw an exception" on the outside.
                WriteLog(ex.ToString());
            }
            else
            {
                WriteLog(string.Format("Non-exception object thrown: {0}", e.ExceptionObject));
            }

            DisplayErrorHandled();
        }


        static void DisplayErrorHandled()
        {
            MessageBox.Show(string.Format("Time.Tracking.Jira encountered an unhandled error. A logfile has been created. If the error continues to occur, please send the logfile content to fnas@seaburysolutions.com.{0}{0}See more details in the logfile: {1}", Environment.NewLine, logPath), "Time.Tracking.Jira");
        }


        static void WriteLog(string message)
        {
            File.AppendAllText(logPath, string.Format("{0}: {1}\n", DateTime.Now, message));
        }
        #endregion
    }
}
