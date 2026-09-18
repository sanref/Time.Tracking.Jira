namespace Time.Tracking.JiraTest
{
    using NUnit.Framework;
    using Time.Tracking.Jira;
    using System;


    [TestFixture]
    public class WatchTimerTest
    {
        private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

        [Test, Description("Time typed in by hand, timer never started: the worklog is dated from the estimate")]
        public void GetInitialStartTime_NeverStarted_FallsBackToTheEstimate()
        {
            var timer = new WatchTimer();
            timer.TimeElapsed = TimeSpan.FromMinutes(30);

            Assert.That(timer.GetInitialStartTime(),
                Is.EqualTo(DateTimeOffset.UtcNow.AddMinutes(-30)).Within(Tolerance));
        }

        [Test, Description("Start, pause, resume: the worklog keeps the first start, not the resume")]
        public void GetInitialStartTime_AfterResume_KeepsTheFirstStart()
        {
            var timer = new WatchTimer();
            timer.SetState(new TimerState
            {
                Running = false,
                TotalTime = TimeSpan.FromHours(1),
                SessionStartTime = DateTime.Now,
                InitialStartTime = DateTimeOffset.UtcNow.AddHours(-3)
            });

            timer.Start();

            Assert.That(timer.GetInitialStartTime(),
                Is.EqualTo(DateTimeOffset.UtcNow.AddHours(-3)).Within(Tolerance));
        }

        [Test, Description("A count started yesterday and logged today is still dated yesterday")]
        public void GetInitialStartTime_StartedYesterday_StaysYesterday()
        {
            var timer = new WatchTimer();
            timer.SetState(new TimerState
            {
                Running = false,
                TotalTime = TimeSpan.FromHours(2),
                SessionStartTime = DateTime.Now.AddHours(-2),
                InitialStartTime = DateTimeOffset.UtcNow.AddDays(-1)
            });

            Assert.That(timer.GetInitialStartTime(),
                Is.EqualTo(DateTimeOffset.UtcNow.AddDays(-1)).Within(Tolerance));
        }

        [Test, Description("Logging empties the clock; the next start is a fresh session, dated from then")]
        public void GetInitialStartTime_AfterClockEmptiedAndRestarted_UsesTheNewStart()
        {
            var timer = new WatchTimer();
            timer.SetState(new TimerState
            {
                Running = false,
                TotalTime = TimeSpan.FromHours(2),
                SessionStartTime = DateTime.Now.AddHours(-2),
                InitialStartTime = DateTimeOffset.UtcNow.AddDays(-1)
            });

            // What posting a worklog that covers the whole clock leaves behind
            timer.TimeElapsed = TimeSpan.Zero;

            timer.Start();

            Assert.That(timer.GetInitialStartTime(),
                Is.EqualTo(DateTimeOffset.UtcNow).Within(Tolerance));
        }

        [Test, Description("Reset then start is a fresh session, dated from then")]
        public void GetInitialStartTime_AfterResetAndRestart_UsesTheNewStart()
        {
            var timer = new WatchTimer();
            timer.SetState(new TimerState
            {
                Running = false,
                TotalTime = TimeSpan.FromHours(2),
                SessionStartTime = DateTime.Now.AddHours(-2),
                InitialStartTime = DateTimeOffset.UtcNow.AddDays(-1)
            });

            timer.Reset();
            timer.Start();

            Assert.That(timer.GetInitialStartTime(),
                Is.EqualTo(DateTimeOffset.UtcNow).Within(Tolerance));
        }
    }
}
