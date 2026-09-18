using NUnit.Framework;
using Time.Tracking.Jira;
using Time.Tracking.Jira.Wpf.Infrastructure;
using Time.Tracking.Jira.Wpf.ViewModels;
using System;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// The guards around the ledger's destructive and Jira-bound actions. None of this could
    /// be reached before <see cref="IDialogService"/> existed: the view model held a Window
    /// and called MessageBox.Show, so every one of these paths needed a message pump.
    /// </summary>
    [TestFixture]
    public class LedgerViewModelTest
    {
        private FakeDialogService dialogs;
        private LedgerViewModel ledger;

        // Settings is a process-wide singleton: whatever a test changes is put back afterwards
        private PauseAndResumeSetting originalPauseOnLock;
        private bool originalAllowMultipleTimers;

        [SetUp]
        public void Setup()
        {
            dialogs = new FakeDialogService();

            originalPauseOnLock = Settings.Instance.PauseOnSessionLock;
            originalAllowMultipleTimers = Settings.Instance.AllowMultipleTimers;

            // Settings is a singleton with a private constructor. Initialize() is never called
            // here, so nothing is loaded from or written to disk.
            ledger = new LedgerViewModel(Settings.Instance, dialogs);
        }

        [TearDown]
        public void TearDown()
        {
            Settings.Instance.PauseOnSessionLock = originalPauseOnLock;
            Settings.Instance.AllowMultipleTimers = originalAllowMultipleTimers;
        }

        private TimeEntryViewModel AddRow(string key, TimeSpan elapsed)
        {
            TimeEntryViewModel entry = new TimeEntryViewModel(key, "Some parent", "", "");
            entry.Timer.TimeElapsed = elapsed;
            ledger.AddEntry(entry);
            return entry;
        }

        #region Log

        [Test, Description("Log: a row with no issue is refused before the dialog opens")]
        public void Log_WithoutIssue_Warns_And_Does_Not_Open_The_Dialog()
        {
            TimeEntryViewModel entry = AddRow("", TimeSpan.FromMinutes(30));

            ledger.Log(entry);

            Assert.That(dialogs.WorklogShown, Is.Zero);
            Assert.That(dialogs.Warnings, Has.Count.EqualTo(1));
            Assert.That(dialogs.Warnings[0], Does.Contain("no issue"));
        }

        [Test, Description("Log: without a Jira session the dialog never opens")]
        public void Log_WithoutSession_Errors_And_Does_Not_Open_The_Dialog()
        {
            TimeEntryViewModel entry = AddRow("FOO-42", TimeSpan.FromMinutes(30));

            ledger.Log(entry);

            Assert.That(dialogs.WorklogShown, Is.Zero);
            Assert.That(dialogs.Errors, Has.Count.EqualTo(1));
            Assert.That(dialogs.Errors[0], Does.Contain("No Jira connection"));
        }

        [Test, Description("Log: the issue guard runs before the session guard")]
        public void Log_WithoutIssue_Reports_The_Issue_Not_The_Session()
        {
            // Both guards would trip on this row. The issue is the one the user can act on
            // without leaving the window, so it is the one that has to be reported.
            TimeEntryViewModel entry = AddRow("", TimeSpan.FromSeconds(20));

            ledger.Log(entry);

            Assert.That(dialogs.Warnings, Has.Count.EqualTo(1));
            Assert.That(dialogs.Errors, Is.Empty);
        }

        // The "less than a minute" guard sits behind the session check, so reaching it needs a
        // live Jira session. It becomes testable once the session is a seam of its own.

        #endregion

        #region Remove

        [Test, Description("Remove: an empty row goes without a question — 4.1.2 behaviour")]
        public void Remove_EmptyRow_Does_Not_Ask()
        {
            TimeEntryViewModel entry = AddRow("", TimeSpan.Zero);

            ledger.Remove(entry);

            Assert.That(dialogs.Questions, Is.Empty);
            Assert.That(ledger.Entries, Does.Not.Contain(entry));
        }

        [Test, Description("Remove: a row with an issue asks, and 'no' keeps it")]
        public void Remove_RowWithIssue_Declined_Keeps_The_Row()
        {
            TimeEntryViewModel entry = AddRow("FOO-42", TimeSpan.Zero);
            dialogs.ConfirmAnswer = false;

            ledger.Remove(entry);

            Assert.That(dialogs.Questions, Has.Count.EqualTo(1));
            Assert.That(ledger.Entries, Does.Contain(entry));
        }

        [Test, Description("Remove: a row with time only — no issue — still asks")]
        public void Remove_RowWithTimeOnly_Asks()
        {
            TimeEntryViewModel entry = AddRow("", TimeSpan.FromMinutes(5));
            dialogs.ConfirmAnswer = true;

            ledger.Remove(entry);

            Assert.That(dialogs.Questions, Has.Count.EqualTo(1));
            Assert.That(ledger.Entries, Does.Not.Contain(entry));
        }

        #endregion

        #region Reset

        [Test, Description("Reset: declining leaves the clock where it was")]
        public void Reset_Declined_Keeps_The_Time()
        {
            TimeEntryViewModel entry = AddRow("FOO-42", TimeSpan.FromMinutes(30));
            dialogs.ConfirmAnswer = false;

            ledger.Reset(entry);

            Assert.That(dialogs.Questions, Has.Count.EqualTo(1));
            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.FromMinutes(30)));
        }

        [Test, Description("Reset: accepting clears it")]
        public void Reset_Accepted_Clears_The_Time()
        {
            TimeEntryViewModel entry = AddRow("FOO-42", TimeSpan.FromMinutes(30));
            dialogs.ConfirmAnswer = true;

            ledger.Reset(entry);

            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.Zero));
        }

        [Test, Description("Reset: a row with no time is a no-op, and asks nothing")]
        public void Reset_WithoutTime_Does_Not_Ask()
        {
            TimeEntryViewModel entry = AddRow("FOO-42", TimeSpan.Zero);

            ledger.Reset(entry);

            Assert.That(dialogs.Questions, Is.Empty);
        }

        #endregion

        #region Add / edit

        [Test, Description("Add issue: cancelling the picker adds no row")]
        public void AddIssue_Cancelled_Adds_Nothing()
        {
            int before = ledger.Entries.Count;
            dialogs.PickIssueAnswer = null;

            ledger.AddIssueCommand.Execute(null);

            Assert.That(dialogs.PickIssueShown, Is.EqualTo(1));
            Assert.That(ledger.Entries, Has.Count.EqualTo(before));
        }

        [Test, Description("Add issue: what the picker returns becomes the new row")]
        public void AddIssue_Accepted_Adds_The_Picked_Issue()
        {
            dialogs.PickIssueAnswer = new IssueSelection
            {
                ParentKey = "FOO-1",
                ParentSummary = "Parent",
                SubtaskKey = "FOO-2",
                SubtaskSummary = "Subtask"
            };

            ledger.AddIssueCommand.Execute(null);

            TimeEntryViewModel added = ledger.Entries[ledger.Entries.Count - 1];
            Assert.That(added.ParentKey, Is.EqualTo("FOO-1"));
            Assert.That(added.SubtaskKey, Is.EqualTo("FOO-2"));
            Assert.That(added.TimerKey, Is.EqualTo("FOO-2"));
        }

        [Test, Description("Edit time: cancelling leaves the clock alone")]
        public void EditTime_Cancelled_Keeps_The_Time()
        {
            TimeEntryViewModel entry = AddRow("FOO-42", TimeSpan.FromMinutes(10));
            dialogs.EditTimeAnswer = null;

            entry.EditTimeCommand.Execute(null);

            Assert.That(dialogs.EditTimeShown, Is.EqualTo(1));
            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.FromMinutes(10)));
        }

        [Test, Description("Edit time: an accepted value replaces the clock")]
        public void EditTime_Accepted_Sets_The_Time()
        {
            TimeEntryViewModel entry = AddRow("FOO-42", TimeSpan.FromMinutes(10));
            dialogs.EditTimeAnswer = TimeSpan.FromMinutes(45);

            entry.EditTimeCommand.Execute(null);

            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.FromMinutes(45)));
        }

        #endregion

        #region Log all

        [Test, Description("Log all: without a Jira session the dialog never opens")]
        public void LogAll_WithoutSession_Errors_And_Does_Not_Open_The_Dialog()
        {
            AddRow("FOO-42", TimeSpan.FromMinutes(30));

            ledger.LogAllCommand.Execute(null);

            Assert.That(dialogs.LogAllShown, Is.Zero);
            Assert.That(dialogs.Errors, Has.Count.EqualTo(1));
            Assert.That(dialogs.Errors[0], Does.Contain("No Jira connection"));
        }

        #endregion

        #region Session lock

        [Test, Description("Lock: with multiple timers, every running row pauses — not just the first (4.1.5)")]
        public void SessionLock_Pauses_Every_Running_Row()
        {
            Settings.Instance.AllowMultipleTimers = true;
            Settings.Instance.PauseOnSessionLock = PauseAndResumeSetting.PauseAndResume;

            TimeEntryViewModel first = AddRow("FOO-1", TimeSpan.FromMinutes(5));
            TimeEntryViewModel second = AddRow("FOO-2", TimeSpan.FromMinutes(5));
            first.Timer.Start();
            second.Timer.Start();

            ledger.HandleSessionLock();

            Assert.That(first.IsRunning, Is.False);
            Assert.That(second.IsRunning, Is.False);
            Assert.That(ledger.HasRunning, Is.False);
        }

        [Test, Description("Unlock: resumes exactly the rows the lock paused, one with no issue included (4.1.5)")]
        public void SessionUnlock_Resumes_The_Rows_The_Lock_Paused()
        {
            Settings.Instance.AllowMultipleTimers = true;
            Settings.Instance.PauseOnSessionLock = PauseAndResumeSetting.PauseAndResume;

            TimeEntryViewModel tagged = AddRow("FOO-1", TimeSpan.FromMinutes(5));
            TimeEntryViewModel untagged = AddRow("", TimeSpan.FromMinutes(5));
            TimeEntryViewModel idle = AddRow("FOO-3", TimeSpan.FromMinutes(5));
            tagged.Timer.Start();
            untagged.Timer.Start();

            ledger.HandleSessionLock();
            ledger.HandleSessionUnlock();

            Assert.That(tagged.IsRunning, Is.True);
            Assert.That(untagged.IsRunning, Is.True, "a row with no issue has an empty key, which used to stop the resume");
            Assert.That(idle.IsRunning, Is.False, "a row that was not running before the lock stays paused");
        }

        [Test, Description("Unlock: two rows sharing a key — the one that was running is the one resumed")]
        public void SessionUnlock_Tells_Rows_With_The_Same_Key_Apart()
        {
            Settings.Instance.PauseOnSessionLock = PauseAndResumeSetting.PauseAndResume;

            TimeEntryViewModel paused = AddRow("FOO-1", TimeSpan.FromMinutes(5));
            TimeEntryViewModel running = AddRow("FOO-1", TimeSpan.FromMinutes(5));
            running.Timer.Start();

            ledger.HandleSessionLock();
            ledger.HandleSessionUnlock();

            Assert.That(running.IsRunning, Is.True);
            Assert.That(paused.IsRunning, Is.False);
        }

        [Test, Description("Unlock: 'Pause' pauses on lock but leaves the rows paused afterwards")]
        public void SessionUnlock_With_Pause_Only_Does_Not_Resume()
        {
            Settings.Instance.PauseOnSessionLock = PauseAndResumeSetting.Pause;

            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(5));
            entry.Timer.Start();

            ledger.HandleSessionLock();
            ledger.HandleSessionUnlock();

            Assert.That(entry.IsRunning, Is.False);
        }

        [Test, Description("Lock: 'No pause' leaves the clocks running")]
        public void SessionLock_With_NoPause_Leaves_The_Rows_Running()
        {
            Settings.Instance.PauseOnSessionLock = PauseAndResumeSetting.NoPause;

            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(5));
            entry.Timer.Start();

            ledger.HandleSessionLock();

            Assert.That(entry.IsRunning, Is.True);
        }

        [Test, Description("A second lock without an unlock in between does not forget what the first paused")]
        public void SessionLock_Twice_Still_Resumes_The_First_Ones()
        {
            Settings.Instance.PauseOnSessionLock = PauseAndResumeSetting.PauseAndResume;

            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(5));
            entry.Timer.Start();

            ledger.HandleSessionLock();
            ledger.HandleSessionLock();
            ledger.HandleSessionUnlock();

            Assert.That(entry.IsRunning, Is.True);
        }

        #endregion

        #region Settling a posted worklog

        [Test, Description("A worklog that took the whole clock resets it, as a fresh session")]
        public void SettlePosted_A_Spent_Clock_Is_Reset()
        {
            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(30));
            int generation = entry.Timer.Generation;

            ledger.SettlePosted(entry, generation, TimeSpan.FromMinutes(30), "");

            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.Zero));
            Assert.That(entry.Timer.Generation, Is.EqualTo(generation + 1));
        }

        [Test, Description("Time counted during the round trip is kept, and the row is paused")]
        public void SettlePosted_Keeps_The_Remainder_And_Pauses()
        {
            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(45));
            entry.Timer.Start();

            ledger.SettlePosted(entry, entry.Timer.Generation, TimeSpan.FromMinutes(30), "");

            Assert.That(entry.IsRunning, Is.False);
            Assert.That(entry.Timer.TimeElapsed.TotalMinutes, Is.EqualTo(15).Within(0.1));
        }

        [Test, Description("A row reset while the post was out is left alone: its new session was never posted")]
        public void SettlePosted_Leaves_A_Row_Reset_Meanwhile_Alone()
        {
            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(30));
            int generation = entry.Timer.Generation;

            entry.Timer.Reset();
            entry.Timer.TimeElapsed = TimeSpan.FromMinutes(10);

            ledger.SettlePosted(entry, generation, TimeSpan.FromMinutes(30), "");

            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.FromMinutes(10)));
        }

        [Test, Description("The comment that went out is spent: it must not come back as a parked note (4.1.5)")]
        public void SettlePosted_Clears_The_Draft_That_Was_Posted()
        {
            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(30));
            entry.Comment = "Reviewed the hour calculation";
            entry.EstimateUpdateMethod = EstimateUpdateMethods.SetTo;
            entry.EstimateUpdateValue = "2h";

            ledger.SettlePosted(entry, entry.Timer.Generation, TimeSpan.FromMinutes(30), "Reviewed the hour calculation");

            Assert.That(entry.Comment, Is.Empty);
            Assert.That(entry.EstimateUpdateMethod, Is.EqualTo(EstimateUpdateMethods.Auto));
            Assert.That(entry.EstimateUpdateValue, Is.Empty);
        }

        [Test, Description("A note that changed while the post was out is a newer one, and stays")]
        public void SettlePosted_Keeps_A_Draft_That_Changed_Meanwhile()
        {
            TimeEntryViewModel entry = AddRow("FOO-1", TimeSpan.FromMinutes(30));
            entry.Comment = "Something newer";

            ledger.SettlePosted(entry, entry.Timer.Generation, TimeSpan.FromMinutes(30), "What was posted");

            Assert.That(entry.Comment, Is.EqualTo("Something newer"));
        }

        #endregion

        #region Reconnecting

        [Test, Description("The wait between tries grows, then settles at five minutes")]
        [TestCase(-1, 15)]
        [TestCase(0, 15)]
        [TestCase(1, 30)]
        [TestCase(2, 60)]
        [TestCase(3, 120)]
        [TestCase(4, 300)]
        [TestCase(50, 300)]
        public void ReconnectDelay_Backs_Off(int failedAttempts, int seconds)
        {
            Assert.That(LedgerViewModel.ReconnectDelay(failedAttempts), Is.EqualTo(TimeSpan.FromSeconds(seconds)));
        }

        #endregion
    }
}
