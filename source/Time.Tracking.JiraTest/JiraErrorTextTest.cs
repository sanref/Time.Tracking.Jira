using NUnit.Framework;
using Time.Tracking.Jira;
using System;
using System.Net;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// Jira's reason for refusing a request, read from the body. Until 4.1.5 a failed worklog
    /// showed an empty error, and at best would have said "HTTP 400 - Bad Request".
    /// </summary>
    [TestFixture]
    public class JiraErrorTextTest
    {
        [Test]
        public void Describe_Reads_ErrorMessages()
        {
            string body = "{\"errorMessages\":[\"Issue does not exist or you do not have permission to see it.\"],\"errors\":{}}";

            Assert.That(JiraErrorText.Describe(body),
                Is.EqualTo("Issue does not exist or you do not have permission to see it."));
        }

        [Test, Description("Per-field errors: the message is for the user, the field name is not")]
        public void Describe_Reads_The_Field_Errors()
        {
            string body = "{\"errorMessages\":[],\"errors\":{\"timeLogged\":\"Worklog must not be null.\"}}";

            Assert.That(JiraErrorText.Describe(body), Is.EqualTo("Worklog must not be null."));
        }

        [Test]
        public void Describe_Joins_Both_One_Per_Line()
        {
            string body = "{\"errorMessages\":[\"First.\"],\"errors\":{\"started\":\"Second.\"}}";

            Assert.That(JiraErrorText.Describe(body), Is.EqualTo("First." + Environment.NewLine + "Second."));
        }

        [Test, Description("The shape some endpoints answer with instead")]
        public void Describe_Reads_A_Single_Message()
        {
            Assert.That(JiraErrorText.Describe("{\"message\":\"Client must be authenticated to access this resource.\",\"status-code\":401}"),
                Is.EqualTo("Client must be authenticated to access this resource."));
        }

        [Test]
        public void Describe_Does_Not_Repeat_A_Message()
        {
            string body = "{\"errorMessages\":[\"Same.\"],\"errors\":{\"x\":\"Same.\"}}";

            Assert.That(JiraErrorText.Describe(body), Is.EqualTo("Same."));
        }

        [Test, Description("A proxy's HTML error page, an empty body, a body with nothing to say")]
        [TestCase("<html><body>502 Bad Gateway</body></html>")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("{\"errorMessages\":[],\"errors\":{}}")]
        [TestCase("[\"not an object\"]")]
        public void Describe_Returns_Null_When_There_Is_No_Reason(string body)
        {
            Assert.That(JiraErrorText.Describe(body), Is.Null);
        }

        [Test, Description("Jira's words first, with the status kept for the log")]
        public void DescribeFailure_Prefers_Jiras_Words()
        {
            string body = "{\"errorMessages\":[\"You do not have the permission to associate a worklog to this issue.\"]}";

            Assert.That(JiraHttpClient.DescribeFailure(HttpStatusCode.Forbidden, "Forbidden", body),
                Is.EqualTo("You do not have the permission to associate a worklog to this issue. (HTTP 403)"));
        }

        [Test, Description("Without them, the status line as before 4.1.5")]
        public void DescribeFailure_Falls_Back_To_The_Status_Line()
        {
            Assert.That(JiraHttpClient.DescribeFailure(HttpStatusCode.BadGateway, "Bad Gateway", "<html></html>"),
                Is.EqualTo("HTTP 502 - Bad Gateway"));
        }
    }
}
