namespace Time.Tracking.JiraTest
{
    using NUnit.Framework;
    using Time.Tracking.Jira;
    using Time.Tracking.Jira.Wpf.ViewModels;
    using System;

    [TestFixture]
    public class WorklogViewModelTest
    {
        private static WorklogViewModel Make()
        {
            return new WorklogViewModel(
                "EAUT-1", "summary",
                new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero),
                TimeSpan.FromHours(1), null, EstimateUpdateMethods.Auto, null);
        }

        [Test]
        public void StartedAt_AcceptsAValidTimeOfDay()
        {
            WorklogViewModel vm = Make();

            vm.StartTimeText = "14:30";

            Assert.That(vm.StartedAt, Is.Not.Null);
            Assert.That(vm.StartedAt.Value.TimeOfDay, Is.EqualTo(new TimeSpan(14, 30, 0)));
            Assert.That(vm.StartedAt.Value.Date, Is.EqualTo(new DateTime(2026, 8, 31)));
        }

        [Test]
        public void StartedAt_AcceptsASingleDigitHour()
        {
            WorklogViewModel vm = Make();

            vm.StartTimeText = "9:05";

            Assert.That(vm.StartedAt.HasValue && vm.StartedAt.Value.TimeOfDay == new TimeSpan(9, 5, 0), Is.True);
        }

        [Test, Description("TimeSpan.Parse would read these as days / >24h; a time of day must not")]
        public void StartedAt_RejectsWhatIsNotAnHhMmTimeOfDay()
        {
            WorklogViewModel vm = Make();

            foreach (string bad in new[] { "", "9", "930", "25:00", "12:60", "1:2:3", "8h", "-1:00" })
            {
                vm.StartTimeText = bad;
                Assert.That(vm.StartedAt, Is.Null, "expected null for [{0}]", bad);
            }
        }
    }
}
