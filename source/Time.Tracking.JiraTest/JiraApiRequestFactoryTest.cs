namespace Time.Tracking.JiraTest
{
    using Moq;
    using NUnit.Framework;
    using Time.Tracking.Jira;
    using System;
    using System.Reflection;

    /// <summary>
    /// Que URL, que verbo y que cuerpo arma cada request.
    /// </summary>
    /// <remarks>
    /// La autenticacion es por header Basic en cada llamada, no por sesion: no existe un POST
    /// de login con usuario y contrasena en el cuerpo. Por eso hasta el "authenticate" es un
    /// GET, y lo unico que lo distingue es el header.
    /// </remarks>
    [TestFixture]
    public class JiraApiRequestFactoryTest
    {
        private const string Username = "Marvin";
        private const string Password = "IThinkItMakesMeHappy";

        /// <summary>base64 de "Marvin:IThinkItMakesMeHappy", escrito a mano para que el test
        /// falle si cambia la forma de codificar y no solo si cambia el codigo que la calcula.</summary>
        private const string ExpectedAuthHeader = "Basic TWFydmluOklUaGlua0l0TWFrZXNNZUhhcHB5";

        private Mock<IJiraRequest> requestMock;
        private Mock<IJiraRequestFactory> requestFactoryMock;

        private JiraApiRequestFactory jiraApiRequestFactory;

        [SetUp]
        public void Setup()
        {
            requestMock = new Mock<IJiraRequest>();

            requestFactoryMock = new Mock<IJiraRequestFactory>();
            requestFactoryMock.Setup(m => m.Create(It.IsAny<string>(), It.IsAny<HttpVerb>())).Returns(requestMock.Object);

            jiraApiRequestFactory = new JiraApiRequestFactory(requestFactoryMock.Object);
        }


        [Test]
        public void CreateValidateSessionRequest_CreatesValidRequest()
        {
            jiraApiRequestFactory.CreateValidateSessionRequest();

            requestFactoryMock.Verify(m => m.Create("/rest/auth/1/session", HttpVerb.Get));
        }


        [Test]
        public void CreateGetIssuesByJQLRequest_CreatesValidRequest()
        {
            jiraApiRequestFactory.CreateGetIssuesByJQLRequest("project = FOO AND status = Open");

            // La JQL va url-encoded, y se piden solo los campos que la grilla usa
            requestFactoryMock.Verify(m => m.Create(
                "/rest/api/3/search/jql?jql=project+%3d+FOO+AND+status+%3d+Open" +
                "&fields=key,summary,timetracking,project,parent&maxResults=200",
                HttpVerb.Get));
        }


        [Test]
        public void CreateGetIssuePickerRequest_CreatesValidRequest()
        {
            jiraApiRequestFactory.CreateGetIssuePickerRequest("FOO-4");

            // currentJQL vacio a proposito: es lo que hace que el autocompletado de Jira busque
            // en todo lo que el usuario puede ver y no dentro de un filtro
            requestFactoryMock.Verify(m => m.Create(
                "/rest/api/3/issue/picker?query=FOO-4&currentJQL=&showSubTasks=true&showSubTaskParent=true",
                HttpVerb.Get));
        }


        [Test]
        public void CreateGetIssueSummaryRequest_CreatesValidRequest()
        {
            jiraApiRequestFactory.CreateGetIssueSummaryRequest("FOO-42");

            requestFactoryMock.Verify(m => m.Create("/rest/api/2/issue/FOO-42", HttpVerb.Get));
        }


        [Test]
        public void CreateGetIssueSummaryRequest_RemoveLeadingAndTrailingSpacesFromIssueKey()
        {
            jiraApiRequestFactory.CreateGetIssueSummaryRequest("   FOO-42   ");

            requestFactoryMock.Verify(m => m.Create("/rest/api/2/issue/FOO-42", HttpVerb.Get));
        }


        [Test]
        public void CreateGetConfigurationRequest_CreatesValidRequest()
        {
            jiraApiRequestFactory.CreateGetConfigurationRequest();

            requestFactoryMock.Verify(m => m.Create("/rest/api/2/configuration", HttpVerb.Get));
        }


        [Test]
        public void CreatePostWorklogRequest_CreatesValidRequest()
        {
            object body = null;
            // AddJsonBody returns void now: the factory never chained off it, and RestSharp's
            // fluent return was the only reason it was not void before.
            requestMock.Setup(m => m.AddJsonBody(It.IsAny<object>()))
                       .Callback<object>(o => body = o);

            var started = new DateTimeOffset(2016, 07, 26, 1, 44, 15, TimeSpan.Zero);

            jiraApiRequestFactory.CreatePostWorklogRequest(
                "FOO-42", started, new TimeSpan(1, 2, 0), "Sorry for the inconvenience...",
                EstimateUpdateMethods.Auto, "");

            requestFactoryMock.Verify(m => m.Create("/rest/api/2/issue/FOO-42/worklog", HttpVerb.Post));

            Assert.That(body, Is.Not.Null, "no se envio cuerpo en el worklog");
            Assert.That(Property(body, "timeSpent"), Is.EqualTo("1h 2m"));
            Assert.That(Property(body, "started"), Is.EqualTo("2016-07-26T01:44:15.000+0000"));
            Assert.That(Property(body, "comment"), Is.EqualTo("Sorry for the inconvenience..."));
        }


        [Test]
        public void CreatePostWorklogRequest_RemoveLeadingAndTrailingSpacesFromIssueKey()
        {
            jiraApiRequestFactory.CreatePostWorklogRequest(
                "   FOO-42   ", DateTimeOffset.UtcNow, new TimeSpan(1, 2, 0), "",
                EstimateUpdateMethods.Auto, "");

            requestFactoryMock.Verify(m => m.Create("/rest/api/2/issue/FOO-42/worklog", HttpVerb.Post));
        }


        [Test]
        [TestCase(EstimateUpdateMethods.Auto, "auto")]
        [TestCase(EstimateUpdateMethods.Leave, "leave")]
        public void CreatePostWorklogRequest_SendsTheAdjustEstimateMode(
            EstimateUpdateMethods method, string expected)
        {
            jiraApiRequestFactory.CreatePostWorklogRequest(
                "FOO-42", DateTimeOffset.UtcNow, new TimeSpan(1, 0, 0), "", method, "");

            requestMock.Verify(m => m.AddQueryParameter("adjustEstimate", expected));
        }


        [Test]
        public void CreatePostWorklogRequest_SetTo_SendsTheNewEstimate()
        {
            jiraApiRequestFactory.CreatePostWorklogRequest(
                "FOO-42", DateTimeOffset.UtcNow, new TimeSpan(1, 0, 0), "",
                EstimateUpdateMethods.SetTo, "3h");

            requestMock.Verify(m => m.AddQueryParameter("adjustEstimate", "new"));
            requestMock.Verify(m => m.AddQueryParameter("newEstimate", "3h"));
        }


        [Test]
        public void CreatePostWorklogRequest_ManualDecrease_SendsTheAmountToReduce()
        {
            jiraApiRequestFactory.CreatePostWorklogRequest(
                "FOO-42", DateTimeOffset.UtcNow, new TimeSpan(1, 0, 0), "",
                EstimateUpdateMethods.ManualDecrease, "30m");

            requestMock.Verify(m => m.AddQueryParameter("adjustEstimate", "manual"));
            requestMock.Verify(m => m.AddQueryParameter("reduceBy", "30m"));
        }


        [Test]
        public void CreateAuthenticateRequest_CreatesValidRequest()
        {
            jiraApiRequestFactory.CreateAuthenticateRequest(Username, Password);

            requestFactoryMock.Verify(m => m.Create("/rest/auth/1/session", HttpVerb.Get));
            requestMock.Verify(m => m.AddHeader("Authorization", ExpectedAuthHeader));
        }


        [Test]
        public void CreateReAuthenticateRequest_IfAuthenticateHasNotBeenCalled_ThrowsException()
        {
            Assert.Throws<AuthenticateNotYetCalledException>(
                () => jiraApiRequestFactory.CreateReAuthenticateRequest());
        }


        [Test]
        public void CreateReAuthenticateRequest_IfAuthenticateHasBeenCalled_ReusesTheCredentials()
        {
            jiraApiRequestFactory.CreateAuthenticateRequest(Username, Password);
            requestMock.Invocations.Clear();

            jiraApiRequestFactory.CreateReAuthenticateRequest();

            requestMock.Verify(m => m.AddHeader("Authorization", ExpectedAuthHeader));
        }


        /// <summary>
        /// El cuerpo es un tipo anonimo, asi que se lee por reflexion. Antes se comparaba
        /// GetHashCode() contra otro anonimo armado en el test, que no dice cual campo difiere
        /// cuando falla.
        /// </summary>
        private static object Property(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, "el cuerpo no tiene la propiedad " + name);
            return property.GetValue(instance);
        }
    }
}
