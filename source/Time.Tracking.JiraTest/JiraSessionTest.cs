using Moq;
using NUnit.Framework;
using Time.Tracking.Jira;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// The connect sequence: authenticate, validate, load the working day. It used to be a
    /// chain of Task/dispatcher callbacks inside LedgerViewModel, so none of its branches
    /// could be reached without a message pump.
    /// </summary>
    [TestFixture]
    public class JiraSessionTest
    {
        private Mock<IJiraApiRequestFactory> requestFactoryMock;
        private Mock<IJiraApiRequester> requesterMock;
        private JiraSession session;

        private TimeTrackingConfiguration originalConfiguration;

        [SetUp]
        public void Setup()
        {
            requestFactoryMock = new Mock<IJiraApiRequestFactory>();
            requesterMock = new Mock<IJiraApiRequester>();

            session = new JiraSession(new JiraClient(requestFactoryMock.Object, requesterMock.Object));

            // Connecting leaves the time tracking configuration in a global static; save it so
            // these tests do not change what JiraTimeHelpersTest sees.
            originalConfiguration = JiraTimeHelpers.Configuration;
        }

        [TearDown]
        public void TearDown()
        {
            JiraTimeHelpers.Configuration = originalConfiguration;
        }

        private void SetupSuccessfulAuthentication(TimeTrackingConfiguration configuration = null)
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>()))
                .Returns(new object());

            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<JiraConfiguration>(It.IsAny<IJiraRequest>()))
                .Returns(new JiraConfiguration { timeTrackingConfiguration = configuration });
        }

        [Test, Description("Connect: the happy path reports Connected")]
        public void Connect_OnSuccess_Reports_Connected()
        {
            SetupSuccessfulAuthentication();

            SessionResult result = session.Connect("user", "token");

            Assert.That(result.State, Is.EqualTo(SessionState.Connected));
            Assert.That(session.SessionValid, Is.True);
        }

        [Test, Description("Connect: a rejected login stops before the session is validated")]
        public void Connect_OnDeniedAuthentication_Reports_AuthenticationFailed()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException());

            SessionResult result = session.Connect("user", "bad-token");

            Assert.That(result.State, Is.EqualTo(SessionState.AuthenticationFailed));
            Assert.That(session.SessionValid, Is.False);
        }

        [Test, Description("Connect: Jira not answering is told apart from Jira saying no — only it is retried (4.1.5)")]
        public void Connect_WhenJiraCannotBeReached_Reports_Unreachable()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException("No such host is known.", true));

            SessionResult result = session.Connect("user", "token");

            Assert.That(result.State, Is.EqualTo(SessionState.Unreachable));
            Assert.That(result.ErrorMessage, Is.EqualTo("No such host is known."));
            Assert.That(session.SessionValid, Is.False);
        }

        [Test, Description("Connect: a refused login with a reason is still a refusal, not a network problem")]
        public void Connect_OnRefusedLogin_Reports_AuthenticationFailed()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException("Invalid username or password", false));

            SessionResult result = session.Connect("user", "bad-token");

            Assert.That(result.State, Is.EqualTo(SessionState.AuthenticationFailed));
        }

        [Test, Description("Connect: Jira's working day is picked up on success")]
        public void Connect_OnSuccess_Stores_The_TimeTracking_Configuration()
        {
            TimeTrackingConfiguration configuration = new TimeTrackingConfiguration
            {
                workingHoursPerDay = 7.5,
                workingHoursPerWeek = 37.5
            };
            SetupSuccessfulAuthentication(configuration);

            session.Connect("user", "token");

            Assert.That(JiraTimeHelpers.Configuration, Is.Not.Null);
            Assert.That(JiraTimeHelpers.Configuration.workingHoursPerDay, Is.EqualTo(7.5));
        }

        [Test, Description("Connect: a Jira that reports no configuration still connects")]
        public void Connect_WithoutConfiguration_Still_Connects()
        {
            SetupSuccessfulAuthentication(null);

            SessionResult result = session.Connect("user", "token");

            Assert.That(result.State, Is.EqualTo(SessionState.Connected));
        }

        [Test, Description("A session that was never connected is not valid")]
        public void SessionValid_IsFalse_Before_Connecting()
        {
            Assert.That(session.SessionValid, Is.False);
        }

        [Test, Description("BaseUrl on a test session is inert rather than a null reference")]
        public void BaseUrl_OnATestSession_Is_Empty()
        {
            session.BaseUrl = "https://example.atlassian.net";

            Assert.That(session.BaseUrl, Is.Empty);
        }
    }
}
