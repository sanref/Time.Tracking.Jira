using NUnit.Framework;
using Time.Tracking.Jira;
using Time.Tracking.Jira.Wpf.ViewModels;
using System;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// What survives a restart. These rules used to sit inside the load and save loops of
    /// LedgerViewModel, next to the collections and the dispatcher, out of reach of a test.
    /// </summary>
    [TestFixture]
    public class LedgerPersistenceTest
    {
        private static GridPersistedRow Row(TimeSpan total, bool running)
        {
            DateTime now = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
            return new GridPersistedRow
            {
                ParentIssue = "FOO-1",
                Subtask = "FOO-2 - Some subtask",
                TimerRunning = running,
                SessionStartTime = now,
                InitialStartTime = now,
                TotalTime = total,
                Pinned = false
            };
        }

        #region display value

        [Test]
        public void SplitDisplayValue_Splits_Key_And_Summary()
        {
            string key, summary;
            LedgerPersistence.SplitDisplayValue("FOO-42 - Fix the thing", out key, out summary);

            Assert.That(key, Is.EqualTo("FOO-42"));
            Assert.That(summary, Is.EqualTo("Fix the thing"));
        }

        [Test, Description("A summary containing the separator keeps everything after the first one")]
        public void SplitDisplayValue_Splits_On_The_First_Separator_Only()
        {
            string key, summary;
            LedgerPersistence.SplitDisplayValue("FOO-42 - Fix A - and B", out key, out summary);

            Assert.That(key, Is.EqualTo("FOO-42"));
            Assert.That(summary, Is.EqualTo("Fix A - and B"));
        }

        [Test, Description("A bare key: a row whose title Jira never returned")]
        public void SplitDisplayValue_WithoutSeparator_Is_All_Key()
        {
            string key, summary;
            LedgerPersistence.SplitDisplayValue("FOO-42", out key, out summary);

            Assert.That(key, Is.EqualTo("FOO-42"));
            Assert.That(summary, Is.Empty);
        }

        [Test]
        public void SplitDisplayValue_Handles_Null_And_Empty()
        {
            string key, summary;

            LedgerPersistence.SplitDisplayValue(null, out key, out summary);
            Assert.That(key, Is.Empty);
            Assert.That(summary, Is.Empty);

            LedgerPersistence.SplitDisplayValue("   ", out key, out summary);
            Assert.That(key, Is.Empty);
            Assert.That(summary, Is.Empty);
        }

        [Test, Description("Round trip: what Join writes, Split reads back")]
        public void Join_And_Split_Round_Trip()
        {
            string joined = LedgerPersistence.JoinDisplayValue("FOO-42", "Fix the thing");

            string key, summary;
            LedgerPersistence.SplitDisplayValue(joined, out key, out summary);

            Assert.That(key, Is.EqualTo("FOO-42"));
            Assert.That(summary, Is.EqualTo("Fix the thing"));
        }

        [Test]
        public void JoinDisplayValue_WithoutKey_Is_Empty()
        {
            Assert.That(LedgerPersistence.JoinDisplayValue("", "Orphan summary"), Is.Empty);
        }

        #endregion

        #region timer state on load

        [Test, Description("NoSave: the time comes back, but never running")]
        public void ToEntry_NoSave_Restores_Time_Paused()
        {
            TimeEntryViewModel entry = LedgerPersistence.ToEntry(
                Row(TimeSpan.FromMinutes(30), true), SaveTimerSetting.NoSave, null);

            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(entry.Timer.Running, Is.False);
        }

        [Test, Description("SavePause: a timer that was running comes back paused")]
        public void ToEntry_SavePause_Restores_Paused()
        {
            TimeEntryViewModel entry = LedgerPersistence.ToEntry(
                Row(TimeSpan.FromMinutes(30), true), SaveTimerSetting.SavePause, null);

            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(entry.Timer.Running, Is.False);
        }

        [Test, Description("SaveRunActive: a timer that was running comes back running")]
        public void ToEntry_SaveRunActive_Restores_Running()
        {
            TimeEntryViewModel entry = LedgerPersistence.ToEntry(
                Row(TimeSpan.FromMinutes(30), true), SaveTimerSetting.SaveRunActive, null);

            Assert.That(entry.Timer.Running, Is.True);
        }

        [Test, Description("SaveRunActive only resumes what was actually running")]
        public void ToEntry_SaveRunActive_Leaves_A_Paused_Row_Paused()
        {
            TimeEntryViewModel entry = LedgerPersistence.ToEntry(
                Row(TimeSpan.FromMinutes(30), false), SaveTimerSetting.SaveRunActive, null);

            Assert.That(entry.Timer.Running, Is.False);
        }

        [Test, Description("A row with an issue but no time comes back, so the issue is not lost")]
        public void ToEntry_WithoutTime_Still_Restores_The_Issue()
        {
            TimeEntryViewModel entry = LedgerPersistence.ToEntry(
                Row(TimeSpan.Zero, false), SaveTimerSetting.SavePause, null);

            Assert.That(entry.ParentKey, Is.EqualTo("FOO-1"));
            Assert.That(entry.SubtaskKey, Is.EqualTo("FOO-2"));
            Assert.That(entry.Timer.TimeElapsed, Is.EqualTo(TimeSpan.Zero));
        }

        [Test, Description("The parent title is not persisted; it comes from the lookup")]
        public void ToEntry_Takes_The_Parent_Summary_From_The_Lookup()
        {
            TimeEntryViewModel entry = LedgerPersistence.ToEntry(
                Row(TimeSpan.Zero, false), SaveTimerSetting.SavePause,
                key => key == "FOO-1" ? "The parent" : "");

            Assert.That(entry.ParentSummary, Is.EqualTo("The parent"));
        }

        [Test, Description("An unresolved parent title is empty, not null")]
        public void ToEntry_WithoutLookup_Leaves_The_Parent_Summary_Empty()
        {
            TimeEntryViewModel entry = LedgerPersistence.ToEntry(
                Row(TimeSpan.Zero, false), SaveTimerSetting.SavePause, null);

            Assert.That(entry.ParentSummary, Is.Empty);
        }

        [Test]
        public void ToEntry_Restores_The_Pin()
        {
            GridPersistedRow row = Row(TimeSpan.Zero, false);
            row.Pinned = true;

            Assert.That(LedgerPersistence.ToEntry(row, SaveTimerSetting.SavePause, null).IsPinned, Is.True);
        }

        #endregion

        #region what gets saved

        [Test, Description("An inline row that was never used is not saved: it would come back forever")]
        public void ToRow_EmptyEntry_Is_Not_Saved()
        {
            TimeEntryViewModel entry = new TimeEntryViewModel("", "", "", "");

            Assert.That(LedgerPersistence.ToRow(entry), Is.Null);
        }

        [Test, Description("An issue with no time is worth keeping")]
        public void ToRow_IssueWithoutTime_Is_Saved()
        {
            TimeEntryViewModel entry = new TimeEntryViewModel("FOO-1", "Parent", "", "");

            Assert.That(LedgerPersistence.ToRow(entry), Is.Not.Null);
        }

        [Test, Description("Time with no issue is worth keeping too: it can be tagged later")]
        public void ToRow_TimeWithoutIssue_Is_Saved()
        {
            TimeEntryViewModel entry = new TimeEntryViewModel("", "", "", "");
            entry.Timer.TimeElapsed = TimeSpan.FromMinutes(5);

            GridPersistedRow row = LedgerPersistence.ToRow(entry);
            Assert.That(row, Is.Not.Null);
            Assert.That(row.TotalTime, Is.EqualTo(TimeSpan.FromMinutes(5)));
        }

        [Test, Description("Save then load returns the same row")]
        public void ToRow_Then_ToEntry_Round_Trips()
        {
            TimeEntryViewModel original = new TimeEntryViewModel("FOO-1", "Parent", "FOO-2", "Subtask");
            original.Timer.TimeElapsed = TimeSpan.FromMinutes(42);
            original.IsPinned = true;

            TimeEntryViewModel restored = LedgerPersistence.ToEntry(
                LedgerPersistence.ToRow(original), SaveTimerSetting.SavePause, key => "Parent");

            Assert.That(restored.ParentKey, Is.EqualTo("FOO-1"));
            Assert.That(restored.ParentSummary, Is.EqualTo("Parent"));
            Assert.That(restored.SubtaskKey, Is.EqualTo("FOO-2"));
            Assert.That(restored.SubtaskSummary, Is.EqualTo("Subtask"));
            Assert.That(restored.IsPinned, Is.True);
            Assert.That(restored.Timer.TimeElapsed, Is.EqualTo(TimeSpan.FromMinutes(42)));
        }

        #endregion
    }
}
