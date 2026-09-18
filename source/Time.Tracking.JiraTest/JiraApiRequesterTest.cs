namespace Time.Tracking.JiraTest
{
    using Moq;
    using NUnit.Framework;
    using Time.Tracking.Jira;
    using System.Net;

    internal class TestPocoClass
    {
        public string foo { get; set; }
        public string bar { get; set; }
    }

    [TestFixture]
    public class JiraApiRequesterTest
    {
        private Mock<IJiraHttpClient> clientMock;
        private Mock<IJiraHttpClientFactory> clientFactoryMock;

        private Mock<IJiraApiRequestFactory> jiraApiRequestFactoryMock;

        private JiraApiRequester jiraApiRequester;

        [SetUp]
        public void Setup()
        {
            clientMock = new Mock<IJiraHttpClient>();

            clientFactoryMock = new Mock<IJiraHttpClientFactory>();
            clientFactoryMock.Setup(c => c.Create(It.IsAny<bool>())).Returns(clientMock.Object);

            jiraApiRequestFactoryMock = new Mock<IJiraApiRequestFactory>();

            jiraApiRequester = new JiraApiRequester(clientFactoryMock.Object, jiraApiRequestFactoryMock.Object);
        }


        [Test, Description("DoAuthenticatedRequest: On OK, it will not try to ReAuthenticate")]
        public void DoAuthenticatedRequest_OnOK_It_Will_Not_Try_To_ReAuhthenticate()
        {
            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(new JiraResponse<TestPocoClass>()
            {
                StatusCode = HttpStatusCode.OK
            });

            var response = jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object);

            jiraApiRequestFactoryMock.Verify(m => m.CreateReAuthenticateRequest(), Times.Never);
        }


        [Test, Description("DoAuthenticatedRequest: On unauthorized, it tries to ReAuthenticate")]
        public void DoAuthenticatedRequest_OnUnauthorized_It_Tries_To_ReAuhthenticate()
        {
            bool authenticated = false;

            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(() =>
                new JiraResponse<TestPocoClass>()
                {
                    StatusCode = authenticated ?  HttpStatusCode.OK : HttpStatusCode.Unauthorized
                }
            );

            clientMock.Setup(c => c.Execute(It.IsAny<IJiraRequest>())).Returns(() => {
                authenticated = true;
                return new JiraResponse()
                {
                    StatusCode = HttpStatusCode.OK
                };
            });

            var response = jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object);

            jiraApiRequestFactoryMock.Verify(m => m.CreateReAuthenticateRequest(), Times.Once);
        }


        [Test, Description("DoAuthenticatedRequest: On BadRequest, it tries to ReAuthenticate")]
        public void DoAuthenticatedRequest_OnBadRequest_It_Tries_To_ReAuhthenticate()
        {
            bool authenticated = false;

            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(() =>
                new JiraResponse<TestPocoClass>()
                {
                    StatusCode = authenticated ?  HttpStatusCode.OK : HttpStatusCode.BadRequest
                }
            );

            clientMock.Setup(c => c.Execute(It.IsAny<IJiraRequest>())).Returns(() => {
                authenticated = true;
                return new JiraResponse()
                {
                    StatusCode = HttpStatusCode.OK
                };
            });

            var response = jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object);

            jiraApiRequestFactoryMock.Verify(m => m.CreateReAuthenticateRequest(), Times.Once);
        }


        [Test, Description("DoAuthenticatedRequest: On unauthorized after ReAuthenticate, it throws an exception")]
        public void DoAuthenticatedRequest_OnUnauthorized_After_ReAuhthenticate_It_Throws_An_Exception()
        {
            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(() =>
                new JiraResponse<TestPocoClass>()
                {
                    StatusCode = HttpStatusCode.Unauthorized
                }
            );

            clientMock.Setup(c => c.Execute(It.IsAny<IJiraRequest>())).Returns(() => {
                return new JiraResponse()
                {
                    StatusCode = HttpStatusCode.OK
                };
            });

            Assert.Throws<RequestDeniedException>(() =>
            {
                var response = jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object);
            });
        }


        [Test, Description("DoAuthenticatedRequest: On ReAuthenticate unauthorized, it throws an exception")]
        public void DoAuthenticatedRequest_On_ReAuthenticate_Unauthorized_It_Throws_An_Exception()
        {
            bool authenticated = false;

            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(() =>
                new JiraResponse<TestPocoClass>()
                {
                    StatusCode = authenticated ?  HttpStatusCode.OK : HttpStatusCode.Unauthorized
                }
            );

            clientMock.Setup(c => c.Execute(It.IsAny<IJiraRequest>())).Returns(() => {
                authenticated = true;
                return new JiraResponse()
                {
                    StatusCode = HttpStatusCode.Unauthorized
                };
            });

            Assert.Throws<RequestDeniedException>(() =>
            {
                var response = jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object);
            });
        }


        [Test, Description("No answer at all — no network, VPN down — is unreachable, and says why (4.1.5)")]
        public void DoAuthenticatedRequest_OnTransportFailure_It_Throws_Unreachable()
        {
            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(new JiraResponse<TestPocoClass>()
            {
                StatusCode = 0,
                ErrorException = new System.Net.Http.HttpRequestException("No such host is known."),
                ErrorMessage = "No such host is known."
            });

            RequestDeniedException denied = Assert.Throws<RequestDeniedException>(() =>
                jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object));

            Assert.That(denied.Unreachable, Is.True);
            Assert.That(denied.Message, Is.EqualTo("No such host is known."));
            jiraApiRequestFactoryMock.Verify(m => m.CreateReAuthenticateRequest(), Times.Never);
        }


        [Test, Description("A Jira that cannot serve right now is worth trying again later")]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.BadGateway)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        public void DoAuthenticatedRequest_OnServerTrouble_It_Throws_Unreachable(HttpStatusCode status)
        {
            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(new JiraResponse<TestPocoClass>()
            {
                StatusCode = status
            });

            RequestDeniedException denied = Assert.Throws<RequestDeniedException>(() =>
                jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object));

            Assert.That(denied.Unreachable, Is.True);
        }


        [Test, Description("A refused request carries Jira's reason, and is not something to retry")]
        public void DoAuthenticatedRequest_OnForbidden_It_Carries_Jiras_Reason()
        {
            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(new JiraResponse<TestPocoClass>()
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorMessage = "You do not have the permission to associate a worklog to this issue. (HTTP 403)"
            });

            RequestDeniedException denied = Assert.Throws<RequestDeniedException>(() =>
                jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object));

            Assert.That(denied.Unreachable, Is.False);
            Assert.That(denied.Message, Does.StartWith("You do not have the permission"));
        }


        [Test, Description("Credentials refused on re-authentication: not unreachable, so never retried on its own")]
        public void DoAuthenticatedRequest_OnReAuthenticate_Unauthorized_It_Is_Not_Unreachable()
        {
            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(new JiraResponse<TestPocoClass>()
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

            clientMock.Setup(c => c.Execute(It.IsAny<IJiraRequest>())).Returns(new JiraResponse()
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

            RequestDeniedException denied = Assert.Throws<RequestDeniedException>(() =>
                jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object));

            Assert.That(denied.Unreachable, Is.False);
            Assert.That(denied.Message, Is.EqualTo("Invalid username or password"));
        }


        [Test, Description("Re-authentication that cannot reach Jira is unreachable too")]
        public void DoAuthenticatedRequest_OnReAuthenticate_TransportFailure_It_Is_Unreachable()
        {
            clientMock.Setup(c => c.Execute<TestPocoClass>(It.IsAny<IJiraRequest>())).Returns(new JiraResponse<TestPocoClass>()
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

            clientMock.Setup(c => c.Execute(It.IsAny<IJiraRequest>())).Returns(new JiraResponse()
            {
                StatusCode = 0,
                ErrorException = new System.Net.Http.HttpRequestException("The operation has timed out."),
                ErrorMessage = "The operation has timed out."
            });

            RequestDeniedException denied = Assert.Throws<RequestDeniedException>(() =>
                jiraApiRequester.DoAuthenticatedRequest<TestPocoClass>(new Mock<IJiraRequest>().Object));

            Assert.That(denied.Unreachable, Is.True);
            Assert.That(denied.Message, Is.EqualTo("The operation has timed out."));
        }
    }
}
