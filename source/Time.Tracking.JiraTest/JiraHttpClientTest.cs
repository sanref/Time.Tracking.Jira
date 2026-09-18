using NUnit.Framework;
using Time.Tracking.Jira;
using System.Text.Json;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// The transport that replaced RestSharp in 4.1.4: how a request becomes a url, and
    /// whether Jira's camelCase JSON still binds to these DTOs now that System.Text.Json
    /// does the reading.
    /// </summary>
    [TestFixture]
    public class JiraHttpClientTest
    {
        private const string BaseUrl = "https://example.atlassian.net";

        /// <summary>BuildUri never touches the HttpClient, so there is no need for one.</summary>
        private static JiraHttpClient Client(string baseUrl = BaseUrl)
        {
            return new JiraHttpClient(null, baseUrl);
        }

        #region BuildUri

        [Test]
        public void BuildUri_Joins_Base_And_Resource()
        {
            JiraRequest request = new JiraRequest("/rest/api/2/configuration", HttpVerb.Get);

            Assert.That(Client().BuildUri(request).ToString(),
                Is.EqualTo("https://example.atlassian.net/rest/api/2/configuration"));
        }

        [Test, Description("A base url the user typed with a trailing slash must not double it")]
        public void BuildUri_Does_Not_Double_The_Separator()
        {
            JiraRequest request = new JiraRequest("/rest/api/2/configuration", HttpVerb.Get);

            Assert.That(Client("https://example.atlassian.net/").BuildUri(request).ToString(),
                Is.EqualTo("https://example.atlassian.net/rest/api/2/configuration"));
        }

        [Test]
        public void BuildUri_Adds_A_Query_Parameter()
        {
            JiraRequest request = new JiraRequest("/rest/api/2/issue/FOO-42/worklog", HttpVerb.Post);
            request.AddQueryParameter("adjustEstimate", "auto");

            Assert.That(Client().BuildUri(request).ToString(),
                Is.EqualTo("https://example.atlassian.net/rest/api/2/issue/FOO-42/worklog?adjustEstimate=auto"));
        }

        [Test, Description("The JQL and picker resources already carry a query string of their own")]
        public void BuildUri_Joins_A_Resource_That_Already_Has_A_Query()
        {
            JiraRequest request = new JiraRequest("/rest/api/3/search/jql?jql=key%20%3D%20FOO-42", HttpVerb.Get);
            request.AddQueryParameter("maxResults", "50");

            Assert.That(Client().BuildUri(request).ToString(), Does.Contain("?jql="));
            Assert.That(Client().BuildUri(request).ToString(), Does.Contain("&maxResults=50"));
        }

        [Test]
        public void BuildUri_Adds_Several_Query_Parameters()
        {
            JiraRequest request = new JiraRequest("/rest/api/2/issue/FOO-42/worklog", HttpVerb.Post);
            request.AddQueryParameter("adjustEstimate", "manual");
            request.AddQueryParameter("reduceBy", "1h");

            Assert.That(Client().BuildUri(request).ToString(),
                Is.EqualTo("https://example.atlassian.net/rest/api/2/issue/FOO-42/worklog?adjustEstimate=manual&reduceBy=1h"));
        }

        #endregion

        #region request accumulation

        [Test]
        public void JiraRequest_Keeps_Headers_And_Body()
        {
            JiraRequest request = new JiraRequest("/rest/api/2/issue/FOO-42/worklog", HttpVerb.Post);
            request.AddHeader("Authorization", "Basic abc");
            request.AddJsonBody(new { timeSpent = "1h" });

            Assert.That(request.Verb, Is.EqualTo(HttpVerb.Post));
            Assert.That(request.Headers, Is.Not.Empty);
            Assert.That(request.JsonBody, Is.Not.Null);
        }

        #endregion

        #region deserialization

        private static T Read<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, JiraHttpClient.JsonOptions);
        }

        [Test, Description("Jira answers camelCase; these DTOs are PascalCase since the pairs were dropped")]
        public void SearchResult_Binds_From_CamelCase_Json()
        {
            SearchResult result = Read<SearchResult>(@"{
                ""issues"": [
                    { ""key"": ""FOO-42"", ""fields"": { ""summary"": ""Fix the thing"" } }
                ],
                ""total"": 1,
                ""startAt"": 0,
                ""maxResults"": 200
            }");

            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Key, Is.EqualTo("FOO-42"));
            Assert.That(result.Issues[0].Fields.Summary, Is.EqualTo("Fix the thing"));
            Assert.That(result.Total, Is.EqualTo(1));
            Assert.That(result.MaxResults, Is.EqualTo(200));
        }

        [Test]
        public void IssuePickerResult_Binds_From_CamelCase_Json()
        {
            IssuePickerResult result = Read<IssuePickerResult>(@"{
                ""sections"": [
                    {
                        ""id"": ""cs"",
                        ""issues"": [ { ""key"": ""FOO-42"", ""summaryText"": ""Fix the thing"" } ]
                    }
                ]
            }");

            Assert.That(result.Sections, Has.Count.EqualTo(1));
            Assert.That(result.Sections[0].Id, Is.EqualTo("cs"));
            Assert.That(result.Sections[0].Issues[0].Key, Is.EqualTo("FOO-42"));
            Assert.That(result.Sections[0].Issues[0].SummaryText, Is.EqualTo("Fix the thing"));
        }

        [Test, Description("This DTO stayed lower case, because JiraTimeHelpers reads it by those names")]
        public void JiraConfiguration_Binds_From_CamelCase_Json()
        {
            JiraConfiguration configuration = Read<JiraConfiguration>(@"{
                ""timeTrackingConfiguration"": {
                    ""workingHoursPerDay"": 7.5,
                    ""workingHoursPerWeek"": 37.5,
                    ""timeFormat"": ""pretty"",
                    ""defaultUnit"": ""minute""
                }
            }");

            Assert.That(configuration.timeTrackingConfiguration, Is.Not.Null);
            Assert.That(configuration.timeTrackingConfiguration.workingHoursPerDay, Is.EqualTo(7.5));
            Assert.That(configuration.timeTrackingConfiguration.defaultUnit, Is.EqualTo("minute"));
        }

        [Test, Description("An issue Jira returned without the fields block must not fault")]
        public void Issue_Without_Fields_Binds_To_Null()
        {
            SearchResult result = Read<SearchResult>(@"{ ""issues"": [ { ""key"": ""FOO-42"" } ] }");

            Assert.That(result.Issues[0].Key, Is.EqualTo("FOO-42"));
            Assert.That(result.Issues[0].Fields, Is.Null);
        }

        [Test, Description("Fields the app does not ask for must not break the binding")]
        public void Unknown_Json_Fields_Are_Ignored()
        {
            SearchResult result = Read<SearchResult>(@"{
                ""expand"": ""schema,names"",
                ""issues"": [ { ""id"": ""10001"", ""self"": ""https://…"", ""key"": ""FOO-42"" } ]
            }");

            Assert.That(result.Issues[0].Key, Is.EqualTo("FOO-42"));
        }

        [Test, Description("The worklog body is what Jira expects, with camelCase names")]
        public void Worklog_Body_Serializes_With_The_Names_Jira_Expects()
        {
            string json = JsonSerializer.Serialize(
                new { timeSpent = "1h 2m", started = "2016-07-26T01:44:15.000+0000", comment = "note" },
                JiraHttpClient.JsonOptions);

            Assert.That(json, Does.Contain("\"timeSpent\""));
            Assert.That(json, Does.Contain("\"started\""));
            Assert.That(json, Does.Contain("\"comment\""));
        }

        #endregion
    }
}
