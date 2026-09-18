using Moq;
using NUnit.Framework;
using Time.Tracking.Jira;
using Time.Tracking.Jira.Wpf.ViewModels;
using System.Collections.Generic;

namespace Time.Tracking.JiraTest
{
    /// <summary>
    /// Which search Jira gets asked, and what the app remembers between asks. KeyJql decided
    /// that split since 4.0.4 and had no test until the catalog was a class of its own.
    /// </summary>
    [TestFixture]
    public class IssueCatalogTest
    {
        private Mock<IJiraApiRequestFactory> requestFactoryMock;
        private Mock<IJiraApiRequester> requesterMock;
        private IssueCatalog catalog;

        [SetUp]
        public void Setup()
        {
            requestFactoryMock = new Mock<IJiraApiRequestFactory>();
            requesterMock = new Mock<IJiraApiRequester>();

            catalog = new IssueCatalog(
                new JiraSession(new JiraClient(requestFactoryMock.Object, requesterMock.Object)));
        }

        #region KeyJql

        [Test, Description("A whole key goes to JQL, upper-cased")]
        public void KeyJql_WholeKey_Builds_A_Key_Query()
        {
            Assert.That(IssueCatalog.KeyJql("foo-42"), Is.EqualTo("key = FOO-42"));
        }

        [Test]
        public void KeyJql_Trims_Surrounding_Space()
        {
            Assert.That(IssueCatalog.KeyJql("  FOO-42  "), Is.EqualTo("key = FOO-42"));
        }

        [Test, Description("Half a key cannot be matched in JQL, so it falls through to the picker")]
        public void KeyJql_PartialKey_Is_Null()
        {
            Assert.That(IssueCatalog.KeyJql("FOO-"), Is.Null);
            Assert.That(IssueCatalog.KeyJql("FOO"), Is.Null);
            Assert.That(IssueCatalog.KeyJql("-42"), Is.Null);
        }

        [Test, Description("Free text is a title search, which JQL is the wrong tool for")]
        public void KeyJql_FreeText_Is_Null()
        {
            Assert.That(IssueCatalog.KeyJql("fix the login bug"), Is.Null);
            Assert.That(IssueCatalog.KeyJql("FOO-42 and more"), Is.Null);
        }

        [Test]
        public void KeyJql_Null_And_Empty_Are_Null()
        {
            Assert.That(IssueCatalog.KeyJql(null), Is.Null);
            Assert.That(IssueCatalog.KeyJql(""), Is.Null);
            Assert.That(IssueCatalog.KeyJql("   "), Is.Null);
        }

        [Test, Description("Underscores and digits are legal in a project key")]
        public void KeyJql_Accepts_Underscores_And_Digits_In_The_Project()
        {
            Assert.That(IssueCatalog.KeyJql("MY_PROJ2-7"), Is.EqualTo("key = MY_PROJ2-7"));
        }

        #endregion

        #region summary cache

        [Test]
        public void Lookup_UnknownKey_Is_Empty()
        {
            Assert.That(catalog.Lookup("FOO-1"), Is.Empty);
        }

        [Test]
        public void Remember_Then_Lookup_Returns_The_Summary()
        {
            catalog.Remember("FOO-1", "The parent");

            Assert.That(catalog.Lookup("FOO-1"), Is.EqualTo("The parent"));
        }

        [Test, Description("An empty summary is not worth caching: the key stays askable")]
        public void Remember_IgnoresEmpty()
        {
            catalog.Remember("FOO-1", "");
            catalog.Remember("FOO-1", null);

            Assert.That(catalog.Lookup("FOO-1"), Is.Empty);
        }

        [Test]
        public void Lookup_Handles_Null_And_Empty_Keys()
        {
            Assert.That(catalog.Lookup(null), Is.Empty);
            Assert.That(catalog.Lookup(""), Is.Empty);
        }

        [Test, Description("A fresh key is worth asking about")]
        public void ShouldRequest_IsTrue_For_A_New_Key()
        {
            Assert.That(catalog.ShouldRequest("FOO-1"), Is.True);
        }

        [Test, Description("A request already in flight is not repeated")]
        public void ShouldRequest_IsFalse_While_Pending()
        {
            catalog.MarkPending("FOO-1");

            Assert.That(catalog.ShouldRequest("FOO-1"), Is.False);
        }

        [Test, Description("A key Jira refused is not asked for again — a redraw would loop")]
        public void ShouldRequest_IsFalse_Once_Unresolved()
        {
            catalog.MarkPending("FOO-1");
            catalog.MarkUnresolved("FOO-1");

            Assert.That(catalog.ShouldRequest("FOO-1"), Is.False);
        }

        [Test, Description("A finished request makes the key askable again")]
        public void ClearPending_Makes_The_Key_Requestable_Again()
        {
            catalog.MarkPending("FOO-1");
            catalog.ClearPending("FOO-1");

            Assert.That(catalog.ShouldRequest("FOO-1"), Is.True);
        }

        [Test, Description("Refresh is an explicit try again: refused keys get another go")]
        public void ForgetUnresolved_Reopens_Refused_Keys()
        {
            catalog.MarkUnresolved("FOO-1");
            catalog.ForgetUnresolved();

            Assert.That(catalog.ShouldRequest("FOO-1"), Is.True);
        }

        [Test, Description("A title that finally arrives clears the refusal")]
        public void Remember_Clears_An_Unresolved_Key()
        {
            catalog.MarkUnresolved("FOO-1");
            catalog.Remember("FOO-1", "The parent");

            Assert.That(catalog.ShouldRequest("FOO-1"), Is.True);
            Assert.That(catalog.Lookup("FOO-1"), Is.EqualTo("The parent"));
        }

        #endregion

        #region search strategy

        [Test, Description("A whole key is looked up with JQL, not the picker")]
        public void Search_WholeKey_Uses_Jql()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<SearchResult>(It.IsAny<IJiraRequest>()))
                .Returns(new SearchResult
                {
                    Issues = new List<Issue>
                    {
                        new Issue { Key = "FOO-42", Fields = new IssueFields { Summary = "Fix it" } }
                    }
                });

            List<IssueItem> found = catalog.Search("FOO-42");

            requestFactoryMock.Verify(m => m.CreateGetIssuesByJQLRequest("key = FOO-42"), Times.Once);
            requestFactoryMock.Verify(m => m.CreateGetIssuePickerRequest(It.IsAny<string>()), Times.Never);
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Key, Is.EqualTo("FOO-42"));
            Assert.That(found[0].Summary, Is.EqualTo("Fix it"));
        }

        [Test, Description("Anything that is not a whole key goes to Jira's autocomplete")]
        public void Search_PartialKey_Uses_The_Picker()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<IssuePickerResult>(It.IsAny<IJiraRequest>()))
                .Returns(new IssuePickerResult
                {
                    Sections = new List<IssuePickerSection>
                    {
                        new IssuePickerSection
                        {
                            Issues = new List<IssuePickerIssue>
                            {
                                new IssuePickerIssue { Key = "FOO-42", SummaryText = "Fix it" }
                            }
                        }
                    }
                });

            List<IssueItem> found = catalog.Search("FOO");

            requestFactoryMock.Verify(m => m.CreateGetIssuePickerRequest("FOO"), Times.Once);
            requestFactoryMock.Verify(m => m.CreateGetIssuesByJQLRequest(It.IsAny<string>()), Times.Never);
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Key, Is.EqualTo("FOO-42"));
        }

        [Test, Description("A key that does not exist is a JQL error, and comes back empty rather than throwing")]
        public void Search_WhenJqlFails_Returns_Empty()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<SearchResult>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException());

            Assert.That(catalog.Search("FOO-42"), Is.Empty);
        }

        [Test, Description("An empty picker section list is not a crash")]
        public void Search_WhenPickerReturnsNothing_Returns_Empty()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<IssuePickerResult>(It.IsAny<IJiraRequest>()))
                .Returns(new IssuePickerResult { Sections = null });

            Assert.That(catalog.Search("FOO"), Is.Empty);
        }

        [Test, Description("Subtasks are asked for by parent, ordered by key")]
        public void Subtasks_Queries_By_Parent()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<SearchResult>(It.IsAny<IJiraRequest>()))
                .Returns(new SearchResult
                {
                    Issues = new List<Issue>
                    {
                        new Issue { Key = "FOO-2", Fields = new IssueFields { Summary = "Subtask" } }
                    }
                });

            string error;
            List<IssueItem> subtasks = catalog.Subtasks("FOO-1", out error);

            requestFactoryMock.Verify(
                m => m.CreateGetIssuesByJQLRequest("parent = FOO-1 ORDER BY key ASC"), Times.Once);
            Assert.That(error, Is.Null);
            Assert.That(subtasks, Has.Count.EqualTo(1));
            Assert.That(subtasks[0].Key, Is.EqualTo("FOO-2"));
        }

        [Test, Description("A failed subtask query reports the error instead of throwing")]
        public void Subtasks_OnFailure_Reports_An_Error()
        {
            requesterMock
                .Setup(m => m.DoAuthenticatedRequest<SearchResult>(It.IsAny<IJiraRequest>()))
                .Throws(new RequestDeniedException());

            string error;
            List<IssueItem> subtasks = catalog.Subtasks("FOO-1", out error);

            Assert.That(subtasks, Is.Empty);
            Assert.That(error, Is.Not.Null);
        }

        #endregion
    }
}
