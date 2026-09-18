using NUnit.Framework;
using Time.Tracking.Jira;
using Time.Tracking.Jira.Wpf.ViewModels;
using System;
using System.Collections.Generic;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// Log all in one dialog (4.1.5): which rows go, how each one's outcome is shown, and what
    /// is left on the rows afterwards. The sending itself is a stand-in here — the view model
    /// only hands the rows over and waits to be told how each one went.
    /// </summary>
    [TestFixture]
    public class LogAllViewModelTest
    {
        private int sends;
        private IList<LogAllRow> sent;
        private Action<LogAllRow, string> posted;
        private Action done;

        [SetUp]
        public void Setup()
        {
            sends = 0;
            sent = null;
            posted = null;
            done = null;
        }

        private void Poster(IList<LogAllRow> rows, Action<LogAllRow, string> onPosted, Action onDone)
        {
            sends++;
            sent = rows;
            posted = onPosted;
            done = onDone;
        }

        private static TimeEntryViewModel Row(string key, int minutes, string comment = "")
        {
            TimeEntryViewModel entry = new TimeEntryViewModel(key, "Parent of " + key, "", "");
            entry.Timer.TimeElapsed = TimeSpan.FromMinutes(minutes);
            entry.Comment = comment;
            return entry;
        }

        private LogAllViewModel Dialog(params TimeEntryViewModel[] entries)
        {
            return new LogAllViewModel(entries, Poster);
        }

        [Test, Description("A row with no issue is listed but cannot be ticked; the others start ticked")]
        public void Rows_Without_An_Issue_Are_Listed_But_Blocked()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30), Row("", 15));

            Assert.That(vm.Rows, Has.Count.EqualTo(2));
            Assert.That(vm.Rows[0].IsSelected, Is.True);
            Assert.That(vm.Rows[0].CanSelect, Is.True);
            Assert.That(vm.Rows[1].IsBlocked, Is.True);
            Assert.That(vm.Rows[1].IsSelected, Is.False);
            Assert.That(vm.Rows[1].CanSelect, Is.False);
            Assert.That(vm.Rows[1].KeyText, Is.EqualTo("No issue"));
        }

        [Test]
        public void The_Header_Counts_The_Ticked_Rows_And_Their_Time()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30), Row("FOO-2", 45), Row("", 15));

            Assert.That(vm.SelectionText, Is.EqualTo("2 of 3 rows selected"));
            Assert.That(vm.SelectedTotalText, Is.EqualTo("1h 15m"));

            vm.Rows[1].IsSelected = false;

            Assert.That(vm.SelectionText, Is.EqualTo("1 of 3 rows selected"));
            Assert.That(vm.SelectedTotalText, Is.EqualTo("0h 30m"));
        }

        [Test, Description("A parked draft comes in as the row's comment, a parked estimate as a note")]
        public void A_Rows_Draft_And_Estimate_Are_Carried_In()
        {
            TimeEntryViewModel entry = Row("FOO-1", 30, "Parked note");
            entry.EstimateUpdateMethod = EstimateUpdateMethods.SetTo;
            entry.EstimateUpdateValue = "2h";

            LogAllViewModel vm = Dialog(entry, Row("FOO-2", 10));

            Assert.That(vm.Rows[0].Comment, Is.EqualTo("Parked note"));
            Assert.That(vm.Rows[0].EstimateNote, Is.EqualTo("Remaining estimate: set to 2h"));
            Assert.That(vm.Rows[1].EstimateNote, Is.Empty, "the automatic estimate needs no note");
        }

        [Test, Description("Only the ticked rows that can go are sent")]
        public void Post_Sends_Only_The_Ticked_Rows()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30), Row("FOO-2", 45), Row("", 15));
            vm.Rows[1].IsSelected = false;

            vm.PostCommand.Execute(null);

            Assert.That(sends, Is.EqualTo(1));
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].IssueKey, Is.EqualTo("FOO-1"));
            Assert.That(sent[0].State, Is.EqualTo(LogAllRowState.Posting));
        }

        [Test, Description("Nothing ticked, nothing to send")]
        public void Post_Is_Off_With_Nothing_Ticked()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30));
            vm.Rows[0].IsSelected = false;

            Assert.That(vm.PostCommand.CanExecute(null), Is.False);
        }

        [Test, Description("While sending: no second send, and no closing")]
        public void While_Posting_It_Cannot_Post_Again_Or_Close()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30));

            vm.PostCommand.Execute(null);

            Assert.That(vm.IsPosting, Is.True);
            Assert.That(vm.CanClose, Is.False);
            Assert.That(vm.PostCommand.CanExecute(null), Is.False);

            vm.PostCommand.Execute(null);
            Assert.That(sends, Is.EqualTo(1));

            done();

            Assert.That(vm.IsPosting, Is.False);
            Assert.That(vm.CanClose, Is.True);
        }

        [Test, Description("Each row shows how it went: logged, or Jira's reason")]
        public void Each_Row_Reports_Its_Own_Outcome()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30), Row("FOO-2", 45));

            vm.PostCommand.Execute(null);
            posted(vm.Rows[0], null);
            posted(vm.Rows[1], "Issue does not exist or you do not have permission to see it. (HTTP 404)");
            done();

            Assert.That(vm.Rows[0].State, Is.EqualTo(LogAllRowState.Logged));
            Assert.That(vm.Rows[0].StatusText, Does.StartWith("Logged"));
            Assert.That(vm.Rows[0].CanSelect, Is.False);

            Assert.That(vm.Rows[1].State, Is.EqualTo(LogAllRowState.Failed));
            Assert.That(vm.Rows[1].StatusText, Does.StartWith("Issue does not exist"));
            Assert.That(vm.Rows[1].IsSelected, Is.True, "a failed row stays ticked, to be sent again");

            Assert.That(vm.FooterText, Is.EqualTo("1 logged · 1 failed"));
        }

        [Test, Description("A failure with no reason still reads as a failure")]
        public void A_Failure_Without_A_Reason_Still_Says_So()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30));

            vm.PostCommand.Execute(null);
            posted(vm.Rows[0], "");

            Assert.That(vm.Rows[0].State, Is.EqualTo(LogAllRowState.Failed));
            Assert.That(vm.Rows[0].StatusText, Is.Not.Empty);
        }

        [Test, Description("Sending again after a failure sends only what failed")]
        public void Posting_Again_Sends_Only_What_Failed()
        {
            LogAllViewModel vm = Dialog(Row("FOO-1", 30), Row("FOO-2", 45));

            vm.PostCommand.Execute(null);
            posted(vm.Rows[0], null);
            posted(vm.Rows[1], "Timed out");
            done();

            vm.PostCommand.Execute(null);

            Assert.That(sends, Is.EqualTo(2));
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].IssueKey, Is.EqualTo("FOO-2"));
        }

        [Test, Description("On close, a comment typed for a row that did not go stays as that row's draft")]
        public void KeepDrafts_Keeps_What_Was_Typed_For_Rows_Not_Logged()
        {
            TimeEntryViewModel logged = Row("FOO-1", 30);
            TimeEntryViewModel untouched = Row("FOO-2", 45, "Old note");
            TimeEntryViewModel unticked = Row("FOO-3", 10);
            LogAllViewModel vm = Dialog(logged, untouched, unticked);

            vm.Rows[0].Comment = "Went out";
            vm.Rows[2].Comment = "  For later  ";
            vm.Rows[2].IsSelected = false;
            vm.Rows[1].IsSelected = false;

            vm.PostCommand.Execute(null);
            posted(vm.Rows[0], null);
            done();

            // What SettlePosted leaves on a logged row
            logged.Comment = "";

            vm.KeepDrafts();

            Assert.That(logged.Comment, Is.Empty, "a logged row's draft went out with it");
            Assert.That(untouched.Comment, Is.EqualTo("Old note"));
            Assert.That(unticked.Comment, Is.EqualTo("For later"));
        }
    }
}
