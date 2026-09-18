namespace Time.Tracking.JiraTest
{
    using Moq;
    using NUnit.Framework;
    using Time.Tracking.Jira;
    using System;
    using System.Collections.Generic;

    [TestFixture]
    public class JiraClientTest
    {
        private Mock<IJiraApiRequestFactory> jiraApiRequestFactoryMock;
        private Mock<IJiraApiRequester> jiraApiRequesterMock;

        private JiraClient jiraClient;

        private TimeTrackingConfiguration originalConfiguration;


        [SetUp]
        public void Setup()
        {
            jiraApiRequestFactoryMock = new Mock<IJiraApiRequestFactory>();

            jiraApiRequesterMock = new Mock<IJiraApiRequester>();

            jiraClient = new JiraClient(jiraApiRequestFactoryMock.Object, jiraApiRequesterMock.Object);

            // Authenticate deja la configuracion de tiempos en un estatico global. Se guarda y
            // se repone para que estos tests no cambien el resultado de JiraTimeHelpersTest,
            // que se apoya en que sea null.
            originalConfiguration = JiraTimeHelpers.Configuration;
        }


        [TearDown]
        public void TearDown()
        {
            JiraTimeHelpers.Configuration = originalConfiguration;
        }


        /// <summary>
        /// Autenticarse dispara dos llamadas: la del propio login y la que trae la
        /// configuracion de tiempos. Las dos tienen que estar preparadas.
        /// </summary>
        private void SetupSuccessfulAuthentication(TimeTrackingConfiguration configuration = null)
        {
            jiraApiRequesterMock
                .Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>()))
                .Returns(new object());

            jiraApiRequesterMock
                .Setup(m => m.DoAuthenticatedRequest<JiraConfiguration>(It.IsAny<IJiraRequest>()))
                .Returns(new JiraConfiguration { timeTrackingConfiguration = configuration });
        }


        [Test, Description("Authenticate returns true on successful authentication")]
        public void Authenticate_OnSuccess_It_Returns_True()
        {
            SetupSuccessfulAuthentication();

            Assert.That(jiraClient.Authenticate("myuser", "mypassword"), Is.True);
        }


        [Test, Description("Authenticate keeps the time tracking configuration Jira reports")]
        public void Authenticate_OnSuccess_It_Stores_The_TimeTracking_Configuration()
        {
            var configuration = new TimeTrackingConfiguration
            {
                workingHoursPerDay = 7.5,
                workingHoursPerWeek = 37.5,
                timeFormat = "pretty",
                defaultUnit = "minute"
            };
            SetupSuccessfulAuthentication(configuration);

            jiraClient.Authenticate("myuser", "mypassword");

            Assert.That(JiraTimeHelpers.Configuration, Is.EqualTo(configuration));
        }


        [Test, Description("Authenticate returns false on unsuccessful authentication")]
        public void Authenticate_OnFailure_It_Returns_False()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>())).Throws<RequestDeniedException>();
            Assert.That(jiraClient.Authenticate("myuser", "mypassword"), Is.False);
        }


        [Test, Description("ValidateSession: On success it sets SessionValid and returns true")]
        public void ValidateSession_OnSuccess_It_Sets_SessionValid_And_Returns_True()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>())).Returns(new object());
            Assert.That(jiraClient.ValidateSession(), Is.True);
            Assert.That(jiraClient.SessionValid, Is.True);
        }


        [Test, Description("ValidateSession: On failure it resets SessionValid and returns false")]
        public void ValidateSession_OnFailure_It_Resets_SessionValid_And_Returns_False()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>())).Throws<RequestDeniedException>();
            Assert.That(jiraClient.ValidateSession(), Is.False);
            Assert.That(jiraClient.SessionValid, Is.False);
        }


        [Test, Description("GetIssuesByJQL: On success it returns a list of type filter")]
        public void GetIssuesByJQL_OnSuccess_It_Returns_List_Of_Issues()
        {
            SearchResult returnData = new SearchResult
            {
                Issues = new List<Issue>()
            };
            returnData.Issues.Add(new Issue { Key = "FOO-1", Fields = new IssueFields { Summary = "Summary for FOO-1" } });
            returnData.Issues.Add(new Issue { Key = "FOO-2", Fields = new IssueFields { Summary = "Summary for FOO-2" } });

            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<SearchResult>(It.IsAny<IJiraRequest>())).Returns(returnData);

            Assert.That(jiraClient.GetIssuesByJQL("testjql"), Is.EqualTo(returnData));
        }


        [Test, Description("GetIssuesByJQL: On failure it returns null")]
        public void GetIssuesByJQL_OnFailure_It_Returns_Null()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<SearchResult>(It.IsAny<IJiraRequest>())).Throws<RequestDeniedException>();
            Assert.That(jiraClient.GetIssuesByJQL("testjql"), Is.Null);
        }


        [Test, Description("GetIssuePickerSuggestions: On success it returns what Jira suggested")]
        public void GetIssuePickerSuggestions_OnSuccess_It_Returns_The_Sections()
        {
            IssuePickerResult returnData = new IssuePickerResult
            {
                Sections = new List<IssuePickerSection>
                {
                    new IssuePickerSection
                    {
                        Id = "cs",
                        Issues = new List<IssuePickerIssue>
                        {
                            new IssuePickerIssue { Key = "FOO-42", SummaryText = "Answer everything" }
                        }
                    }
                }
            };

            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<IssuePickerResult>(It.IsAny<IJiraRequest>())).Returns(returnData);

            Assert.That(jiraClient.GetIssuePickerSuggestions("FOO"), Is.EqualTo(returnData));
        }


        [Test, Description("GetIssuePickerSuggestions: On failure it returns null")]
        public void GetIssuePickerSuggestions_OnFailure_It_Returns_Null()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<IssuePickerResult>(It.IsAny<IJiraRequest>())).Throws<RequestDeniedException>();
            Assert.That(jiraClient.GetIssuePickerSuggestions("FOO"), Is.Null);
        }


        [Test, Description("GetIssueSummary: On success it returns a list of type filter")]
        public void GetIssueSummary_OnSuccess_It_Returns_Issue_Summary()
        {
            Issue returnData = new Issue
            {
                Fields = new IssueFields
                {
                    Summary = "The long dark tea-time of the soul"
                }
            };

            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<Issue>(It.IsAny<IJiraRequest>())).Returns(returnData);

            Assert.That(jiraClient.GetIssueSummary("DG-42"), Is.EqualTo(returnData.Fields.Summary));
        }


        [Test, Description("GetIssueSummary: a denied request reaches the caller")]
        public void GetIssueSummary_OnFailure_It_Propagates_The_Exception()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<Issue>(It.IsAny<IJiraRequest>())).Throws<RequestDeniedException>();

            // A diferencia del resto de JiraClient, aca el error no se traduce a un valor
            // neutro: quien pide el resumen (LedgerViewModel) lo atrapa y lo registra, porque
            // necesita distinguir "sin resumen" de "no se pudo consultar".
            Assert.Throws<RequestDeniedException>(() => jiraClient.GetIssueSummary("DG-42"));
        }


        [Test, Description("GetIssueSummary: an issue with no fields comes back empty")]
        public void GetIssueSummary_WithoutFields_It_Returns_Empty_String()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<Issue>(It.IsAny<IJiraRequest>())).Returns(new Issue());

            Assert.That(jiraClient.GetIssueSummary("DG-42"), Is.EqualTo(""));
        }


        [Test, Description("GetTimeTrackingConfiguration: On failure it returns null")]
        public void GetTimeTrackingConfiguration_OnFailure_It_Returns_Null()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<JiraConfiguration>(It.IsAny<IJiraRequest>())).Throws<RequestDeniedException>();

            Assert.That(jiraClient.GetTimeTrackingConfiguration(), Is.Null);
        }


        [Test, Description("PostWorklog: On success it returns true")]
        public void PostWorklog_OnSuccess_It_Returns_True()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>())).Returns(new object());

            string error;
            Assert.That(jiraClient.PostWorklog("DG-42", DateTimeOffset.UtcNow, new TimeSpan(1, 20, 0), "Time is an illusion", EstimateUpdateMethods.Auto, null, out error), Is.True);
            Assert.That(error, Is.Null);
        }


        [Test, Description("PostWorklog: On failure it returns false")]
        public void PostWorklog_OnFailure_It_Returns_False()
        {
            jiraApiRequesterMock.Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>())).Throws<RequestDeniedException>();

            string error;
            Assert.That(jiraClient.PostWorklog("DG-42", DateTimeOffset.UtcNow, new TimeSpan(2, 10, 0), "Lunchtime doubly so", EstimateUpdateMethods.Auto, null, out error), Is.False);
        }


        [Test, Description("PostWorklog: a failure says why — what the user reads in the error dialog (4.1.5)")]
        public void PostWorklog_OnFailure_It_Returns_The_Reason()
        {
            jiraApiRequesterMock
                .Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException("Issue does not exist or you do not have permission to see it. (HTTP 404)", false));

            string error;
            jiraClient.PostWorklog("DG-42", DateTimeOffset.UtcNow, new TimeSpan(0, 30, 0), "", EstimateUpdateMethods.Auto, null, out error);

            Assert.That(error, Is.EqualTo("Issue does not exist or you do not have permission to see it. (HTTP 404)"));
        }


        [Test, Description("A failed search keeps its reason for the log, instead of an empty one")]
        public void GetIssuesByJQL_OnFailure_It_Keeps_The_Reason()
        {
            jiraApiRequesterMock
                .Setup(m => m.DoAuthenticatedRequest<SearchResult>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException("The value 'NOPE-1' does not exist for the field 'key'. (HTTP 400)", false));

            Assert.That(jiraClient.GetIssuesByJQL("key = NOPE-1"), Is.Null);
            Assert.That(jiraClient.ErrorMessage, Does.Contain("NOPE-1"));
        }


        [Test, Description("Authenticate: telling 'Jira did not answer' from 'Jira said no' is what decides a retry")]
        [TestCase(true)]
        [TestCase(false)]
        public void Authenticate_OnFailure_It_Reports_Whether_Jira_Was_Unreachable(bool unreachable)
        {
            jiraApiRequesterMock
                .Setup(m => m.DoAuthenticatedRequest<object>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException("whatever", unreachable));

            Assert.That(jiraClient.Authenticate("myuser", "mypassword"), Is.False);
            Assert.That(jiraClient.Unreachable, Is.EqualTo(unreachable));
        }


    }
}
