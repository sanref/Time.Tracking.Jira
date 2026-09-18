namespace Time.Tracking.JiraTest
{
    using NUnit.Framework;
    using Time.Tracking.Jira;
    using Time.Tracking.Jira.Wpf.ViewModels;
    using Time.Tracking.Jira.Wpf.Views;
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Threading;

    /// <summary>
    /// The worklog dialog carries the only <see cref="DatePicker"/> in the app, with a
    /// hand-written template and Calendar style. BAML compilation does not exercise a template;
    /// showing the window off screen and opening the calendar does. Log all's dialog is shown
    /// here too: the WPF Application these need belongs to one thread, so they share a fixture.
    /// </summary>
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public class WorklogWindowRenderTest
    {
        [OneTimeSetUp]
        public void LoadTheme()
        {
            // Constructing Application registers the pack:// scheme and gives the merged
            // dictionaries somewhere app-wide to live, the same as Program.Main does.
            if (Application.Current == null)
                new Application();

            // By default the Application shuts down with its last window, and every test here
            // opens and closes one: the second test would find it on its way out.
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            foreach (string name in new[] { "Tokens", "Controls", "Forms" })
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        string.Format("pack://application:,,,/Time.Tracking.Jira;component/Wpf/Theme/{0}.xaml", name),
                        UriKind.Absolute)
                });
            }
        }

        [Test]
        public void TheDialogAndItsCalendarRenderWithoutThrowing()
        {
            WorklogViewModel vm = new WorklogViewModel(
                "EAUT-1", "summary",
                new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero),
                TimeSpan.FromHours(1), null, EstimateUpdateMethods.Auto, null);

            WorklogWindow window = new WorklogWindow(vm)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowActivated = false
            };

            window.Show();
            Pump();

            DatePicker picker = FindChild<DatePicker>(window);
            Assert.That(picker, Is.Not.Null, "the dialog should contain a DatePicker");

            picker.IsDropDownOpen = true;
            Pump();
            picker.IsDropDownOpen = false;
            Pump();

            window.Close();
            Pump();
        }

        [Test, Description("Log all (4.1.5): every row state the list can show, rendered for real")]
        public void TheLogAllDialogRendersEveryRowState()
        {
            TimeEntryViewModel ready = new TimeEntryViewModel("EAUT-1", "Parent", "EAUT-2", "A subtask");
            ready.Timer.TimeElapsed = TimeSpan.FromMinutes(90);
            ready.EstimateUpdateMethod = EstimateUpdateMethods.SetTo;
            ready.EstimateUpdateValue = "2h";

            TimeEntryViewModel failed = new TimeEntryViewModel("EAUT-3", "Another parent", "", "");
            failed.Timer.TimeElapsed = TimeSpan.FromMinutes(20);
            failed.Comment = "A parked note";

            TimeEntryViewModel logged = new TimeEntryViewModel("EAUT-4", "Third", "", "");
            logged.Timer.TimeElapsed = TimeSpan.FromMinutes(5);

            TimeEntryViewModel blocked = new TimeEntryViewModel("", "", "", "");
            blocked.Timer.TimeElapsed = TimeSpan.FromMinutes(12);

            LogAllViewModel vm = new LogAllViewModel(
                new[] { ready, failed, logged, blocked },
                (rows, posted, done) => { });

            LogAllWindow window = new LogAllWindow(vm)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowActivated = false
            };

            window.Show();
            Pump();

            vm.Rows[1].Error = "Issue does not exist or you do not have permission to see it. (HTTP 404)";
            vm.Rows[1].State = LogAllRowState.Failed;
            vm.Rows[2].State = LogAllRowState.Logged;
            Pump();

            Assert.That(FindChild<CheckBox>(window), Is.Not.Null, "the rows should have rendered");

            window.Close();
            Pump();
        }

        private static void Pump()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;

                T found = FindChild<T>(child);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
