namespace Time.Tracking.JiraTest
{
    using NUnit.Framework;
    using Time.Tracking.Jira;
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// La red de seguridad de la migracion a .NET: al actualizar desde una version sobre .NET
    /// Framework, la configuracion del usuario tiene que aparecer intacta.
    /// </summary>
    /// <remarks>
    /// Los fixtures de Fixtures\ son user.config con la forma real que escribia
    /// System.Configuration. El blob de GridPersistedRows es autentico, tomado de una
    /// instalacion 4.0.5 en uso: no se puede generar uno nuevo porque BinaryFormatter, que era
    /// lo unico capaz de escribir ese formato, ya no existe. Usuario y contrasena si estan
    /// reemplazados por valores de prueba.
    /// </remarks>
    [TestFixture]
    public class SettingsMigrationTest
    {
        private string tempDir;

        [SetUp]
        public void Setup()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "TimeTrackingJiraTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(tempDir, true); }
            catch (IOException) { }
        }

        private static string Fixture(string name)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
        }


        #region importacion desde el user.config de la propia aplicacion

        [Test]
        public void Import_RecoversEveryScalarSetting()
        {
            SettingsData data = UserConfigImporter.Read(Fixture("sea-stopwatch-4.0-user.config"));

            Assert.Multiple(() =>
            {
                Assert.That(data.JiraBaseUrl, Is.EqualTo("https://example.atlassian.net/"));
                Assert.That(data.Username, Is.EqualTo("marvin@example.com"));
                Assert.That(data.IssueCount, Is.EqualTo(8));
                Assert.That(data.AlwaysOnTop, Is.True);
                Assert.That(data.MinimizeToTray, Is.True);
                Assert.That(data.IncludeProjectName, Is.True);
                Assert.That(data.AllowMultipleTimers, Is.False);
                Assert.That(data.FirstRun, Is.False);
                Assert.That(data.LoggingEnabled, Is.True);
                Assert.That(data.CurrentFilter, Is.EqualTo(12479));
                Assert.That(data.StartTransitions, Is.EqualTo("In Progress"));
                Assert.That(data.ViewWindowSizes, Is.EqualTo("Ledger=680x724;Grid=900x640"));
            });
        }


        [Test]
        public void Import_RecoversTheEnumSettingsByTheirStoredNumber()
        {
            SettingsData data = UserConfigImporter.Read(Fixture("sea-stopwatch-4.0-user.config"));

            Assert.Multiple(() =>
            {
                Assert.That(data.SaveTimerState, Is.EqualTo(SaveTimerSetting.SavePause));
                Assert.That(data.PauseOnSessionLock, Is.EqualTo(PauseAndResumeSetting.PauseAndResume));
                Assert.That(data.PostWorklogComment, Is.EqualTo(WorklogCommentSetting.WorklogOnly));
                Assert.That(data.StartupForm, Is.EqualTo(StartupFormSetting.Grid));
            });
        }


        [Test, Description("Las filas de la grilla, que estaban en base64 escrito por BinaryFormatter")]
        public void Import_RecoversTheGridRows()
        {
            SettingsData data = UserConfigImporter.Read(Fixture("sea-stopwatch-4.0-user.config"));

            // El arreglo interno de List<T> tiene 8 posiciones y solo 7 con datos: si se
            // ignorara _size entraria una fila nula de relleno.
            Assert.That(data.GridPersistedRows, Has.Count.EqualTo(7));

            GridPersistedRow first = data.GridPersistedRows[0];
            Assert.Multiple(() =>
            {
                Assert.That(first.ParentIssue, Is.EqualTo("AVEMTST-303"));
                Assert.That(first.Subtask, Is.EqualTo("AVEMTST-1591 - Analysis"));
                Assert.That(first.TimerRunning, Is.False);
                // Hasta la fraccion de segundo: es tiempo que todavia no se registro en Jira
                Assert.That(first.TotalTime, Is.EqualTo(TimeSpan.FromTicks(910091037)));
                // Con los ticks exactos: el decodificador no puede redondear un instante
                Assert.That(first.InitialStartTime, Is.EqualTo(
                    new DateTimeOffset(2026, 8, 6, 14, 11, 11, TimeSpan.Zero).AddTicks(4056251)));
                Assert.That(first.SessionStartTime, Is.EqualTo(
                    new DateTime(2026, 8, 6, 13, 19, 43, 75).AddTicks(1266)));
            });
        }


        [Test, Description("Una fila que nunca se inicio no tiene InitialStartTime")]
        public void Import_KeepsRowsWithoutAnInitialStartTime()
        {
            SettingsData data = UserConfigImporter.Read(Fixture("sea-stopwatch-4.0-user.config"));

            GridPersistedRow row = data.GridPersistedRows[3];
            Assert.Multiple(() =>
            {
                Assert.That(row.ParentIssue, Is.EqualTo("EATST-14059"));
                Assert.That(row.Subtask, Is.Empty);
                Assert.That(row.InitialStartTime, Is.Null);
                Assert.That(row.TotalTime, Is.EqualTo(TimeSpan.FromHours(4)));
            });
        }


        [Test, Description("Ninguna fila se pierde: la suma de tiempo no registrado se conserva")]
        public void Import_KeepsEveryRowsUnloggedTime()
        {
            SettingsData data = UserConfigImporter.Read(Fixture("sea-stopwatch-4.0-user.config"));

            TimeSpan total = TimeSpan.Zero;
            foreach (GridPersistedRow row in data.GridPersistedRows)
                total += row.TotalTime;

            Assert.That(total, Is.EqualTo(
                TimeSpan.FromTicks(910091037)      // AVEMTST-303
                + TimeSpan.FromTicks(140167897)    // EAUT-51992
                + TimeSpan.FromTicks(338159718)    // EAUT-50611
                + TimeSpan.FromHours(4)            // EATST-14059
                + TimeSpan.FromMinutes(7)          // EATST-14060
                + TimeSpan.FromHours(1)            // EATST-14061
                + TimeSpan.FromMinutes(10)));      // EATST-14062
        }


        [Test, Description("La contrasena viaja cifrada, sin pasar por texto plano")]
        public void Import_CopiesThePasswordWithoutDecryptingIt()
        {
            string encrypted = DPAPI.Encrypt("IThinkItMakesMeHappy");
            string path = Path.Combine(tempDir, "user.config");
            File.WriteAllText(path, UserConfig(new Dictionary<string, string>
            {
                { "Username", "marvin@example.com" },
                { "Password", encrypted }
            }));

            SettingsData data = UserConfigImporter.Read(path);

            Assert.That(data.Password, Is.EqualTo(encrypted));
            Assert.That(DPAPI.Decrypt(data.Password), Is.EqualTo("IThinkItMakesMeHappy"));
        }


        [Test, Description("Un user.config muy viejo sin IssueCount no deja la grilla sin filas")]
        public void Import_FallsBackToTheDefaultIssueCount()
        {
            string path = Path.Combine(tempDir, "user.config");
            File.WriteAllText(path, UserConfig(new Dictionary<string, string>
            {
                { "JiraBaseUrl", "https://example.atlassian.net/" }
            }));

            Assert.That(UserConfigImporter.Read(path).IssueCount, Is.EqualTo(6));
        }


        [Test]
        public void Import_WithoutGridRows_ReturnsAnEmptyList()
        {
            string path = Path.Combine(tempDir, "user.config");
            File.WriteAllText(path, UserConfig(new Dictionary<string, string>
            {
                { "GridPersistedRows", "" }
            }));

            Assert.That(UserConfigImporter.Read(path).GridPersistedRows, Is.Empty);
        }


        [Test, Description("Un blob ilegible no puede impedir que la aplicacion abra")]
        public void Import_WithACorruptGridBlob_ReturnsAnEmptyListInsteadOfThrowing()
        {
            string path = Path.Combine(tempDir, "user.config");
            File.WriteAllText(path, UserConfig(new Dictionary<string, string>
            {
                { "JiraBaseUrl", "https://example.atlassian.net/" },
                { "GridPersistedRows", "bm90IGFuIE5SQkYgcGF5bG9hZA==" }
            }));

            SettingsData data = UserConfigImporter.Read(path);

            Assert.That(data.GridPersistedRows, Is.Empty);
            Assert.That(data.JiraBaseUrl, Is.EqualTo("https://example.atlassian.net/"),
                "el resto de la configuracion se tiene que recuperar igual");
        }

        #endregion


        #region eleccion del user.config de origen

        [Test, Description("Se elige el ultimo que uso el usuario, no el de version mas alta")]
        public void FindNewest_PicksTheMostRecentlyWrittenConfig()
        {
            string abandonedButHigher = Write("Sea.StopWatch.exe_Url_aaa/9.9.9.9/user.config",
                DateTime.UtcNow.AddYears(-1));
            string lastUsed = Write("Sea.StopWatch.exe_Url_bbb/4.0.0.0/user.config",
                DateTime.UtcNow);

            Assert.That(UserConfigFile.FindNewest(tempDir), Is.EqualTo(lastUsed));
            Assert.That(UserConfigFile.FindNewest(tempDir), Is.Not.EqualTo(abandonedButHigher));
        }


        [Test, Description("Se busca en todas las carpetas del ejecutable, no solo en una")]
        public void FindNewest_LooksAcrossEveryInstallPath()
        {
            Write("Sea.StopWatch.exe_Url_aaa/4.0.0.0/user.config", DateTime.UtcNow.AddDays(-3));
            string newest = Write("Sea.StopWatch.exe_Url_zzz/3.0.2.0/user.config", DateTime.UtcNow);

            Assert.That(UserConfigFile.FindNewest(tempDir), Is.EqualTo(newest));
        }


        [Test]
        public void FindNewest_IgnoresFoldersThatAreNotAnAssemblyVersion()
        {
            Write("Sea.StopWatch.exe_Url_aaa/backup/user.config", DateTime.UtcNow);

            Assert.That(UserConfigFile.FindNewest(tempDir), Is.Null);
        }


        [Test]
        public void FindNewest_WithoutAnyPreviousInstall_ReturnsNull()
        {
            Assert.That(UserConfigFile.FindNewest(Path.Combine(tempDir, "no-existe")), Is.Null);
        }

        #endregion


        #region ida y vuelta por el archivo nuevo

        [Test, Description("Lo importado se guarda y se relee sin perder nada")]
        public void SettingsStore_RoundTripsWhatWasImported()
        {
            SettingsData imported = UserConfigImporter.Read(Fixture("sea-stopwatch-4.0-user.config"));
            string path = Path.Combine(tempDir, "settings.json");

            SettingsStore.SaveTo(path, imported);
            SettingsData reloaded = SettingsStore.LoadFrom(path);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.JiraBaseUrl, Is.EqualTo(imported.JiraBaseUrl));
                Assert.That(reloaded.Username, Is.EqualTo(imported.Username));
                Assert.That(reloaded.IssueCount, Is.EqualTo(imported.IssueCount));
                Assert.That(reloaded.StartupForm, Is.EqualTo(imported.StartupForm));
                Assert.That(reloaded.PauseOnSessionLock, Is.EqualTo(imported.PauseOnSessionLock));
                Assert.That(reloaded.GridPersistedRows, Has.Count.EqualTo(7));
            });

            for (int i = 0; i < imported.GridPersistedRows.Count; i++)
            {
                GridPersistedRow before = imported.GridPersistedRows[i];
                GridPersistedRow after = reloaded.GridPersistedRows[i];

                Assert.Multiple(() =>
                {
                    Assert.That(after.ParentIssue, Is.EqualTo(before.ParentIssue));
                    Assert.That(after.Subtask, Is.EqualTo(before.Subtask));
                    Assert.That(after.TimerRunning, Is.EqualTo(before.TimerRunning));
                    Assert.That(after.TotalTime, Is.EqualTo(before.TotalTime));
                    Assert.That(after.InitialStartTime, Is.EqualTo(before.InitialStartTime));
                    Assert.That(after.SessionStartTime, Is.EqualTo(before.SessionStartTime));
                });
            }
        }


        [Test]
        public void SettingsStore_WithoutAFile_ReturnsTheDefaults()
        {
            SettingsData data = SettingsStore.LoadFrom(Path.Combine(tempDir, "no-existe.json"));

            Assert.Multiple(() =>
            {
                Assert.That(data.IssueCount, Is.EqualTo(6));
                Assert.That(data.FirstRun, Is.True);
                Assert.That(data.StartupForm, Is.EqualTo(StartupFormSetting.Ledger));
                Assert.That(data.GridPersistedRows, Is.Empty);
            });
        }


        [Test, Description("Un settings.json truncado no puede dejar la aplicacion sin abrir")]
        public void SettingsStore_WithACorruptFile_ReturnsTheDefaults()
        {
            string path = Path.Combine(tempDir, "settings.json");
            File.WriteAllText(path, "{ \"JiraBaseUrl\": \"https://example.at");

            Assert.That(SettingsStore.LoadFrom(path).IssueCount, Is.EqualTo(6));
        }


        [Test, Description("Un settings.json ilegible se copia aparte antes de que el autoguardado lo pise (4.1.5)")]
        public void SettingsStore_WithACorruptFile_KeepsACopy()
        {
            string path = Path.Combine(tempDir, "settings.json");
            string broken = "{ \"JiraBaseUrl\": \"https://example.at";
            File.WriteAllText(path, broken);

            string setAside;
            SettingsStore.LoadFrom(path, out setAside);

            Assert.That(setAside, Is.Not.Null);
            Assert.That(Path.GetFileName(setAside), Does.StartWith("settings.json.corrupt-"));
            Assert.That(File.ReadAllText(setAside), Is.EqualTo(broken));

            // Copiado, no movido: sin settings.json el proximo arranque reimportaria el
            // user.config de la version anterior
            Assert.That(File.Exists(path), Is.True);
        }


        [Test]
        public void SettingsStore_WithoutAFile_KeepsNoCopy()
        {
            string setAside;
            SettingsStore.LoadFrom(Path.Combine(tempDir, "no-existe.json"), out setAside);

            Assert.That(setAside, Is.Null);
            Assert.That(Directory.GetFiles(tempDir), Is.Empty);
        }


        [Test]
        public void SettingsStore_WithAGoodFile_KeepsNoCopy()
        {
            string path = Path.Combine(tempDir, "settings.json");
            SettingsStore.SaveTo(path, new SettingsData { IssueCount = 9 });

            string setAside;
            SettingsData data = SettingsStore.LoadFrom(path, out setAside);

            Assert.That(data.IssueCount, Is.EqualTo(9));
            Assert.That(setAside, Is.Null);
            Assert.That(Directory.GetFiles(tempDir), Has.Length.EqualTo(1));
        }

        #endregion


        #region importacion desde el "Jira StopWatch" original

        [Test]
        public void LegacyImport_RecoversTheSettings()
        {
            LegacyImport.Result result = LegacyImport.Load(Fixture("jirastopwatch-2.x-user.config"));

            Assert.Multiple(() =>
            {
                Assert.That(result.BaseUrl, Is.EqualTo("https://legacy.example.com/"));
                Assert.That(result.Username, Is.EqualTo("zaphod@example.com"));
                Assert.That(result.IssueCount, Is.EqualTo(6));
                Assert.That(result.OnExit, Is.EqualTo(SaveTimerSetting.SaveRunActive));
                Assert.That(result.OnSessionLock, Is.EqualTo(PauseAndResumeSetting.Pause));
                Assert.That(result.CommentMode, Is.EqualTo(WorklogCommentSetting.WorklogAndComment));
                Assert.That(result.TokenDecryptFailed, Is.False);
            });
        }


        [Test, Description("Las filas vacias de la MainForm vieja no se traen")]
        public void LegacyImport_DropsTheBlankRows()
        {
            LegacyImport.Result result = LegacyImport.Load(Fixture("jirastopwatch-2.x-user.config"));

            // El blob trae seis ranuras, todas sin issue y sin tiempo
            Assert.That(result.Rows, Is.Empty);
        }

        #endregion


        /// <summary>Un user.config minimo con los settings indicados.</summary>
        private static string UserConfig(Dictionary<string, string> values)
        {
            System.Text.StringBuilder xml = new System.Text.StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.AppendLine("<configuration><userSettings><StopWatch.Properties.Settings>");

            foreach (KeyValuePair<string, string> pair in values)
                xml.AppendFormat("<setting name=\"{0}\" serializeAs=\"String\"><value>{1}</value></setting>",
                    pair.Key, pair.Value);

            xml.AppendLine("</StopWatch.Properties.Settings></userSettings></configuration>");
            return xml.ToString();
        }


        /// <summary>Crea un user.config vacio en <paramref name="relativePath"/> con esa fecha.</summary>
        private string Write(string relativePath, DateTime writtenUtc)
        {
            string path = Path.Combine(tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, UserConfig(new Dictionary<string, string>()));
            File.SetLastWriteTimeUtc(path, writtenUtc);
            return path;
        }
    }
}
