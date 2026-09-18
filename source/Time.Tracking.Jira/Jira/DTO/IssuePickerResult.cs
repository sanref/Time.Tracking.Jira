using System.Collections.Generic;

// One property per field. Until 4.1.4 each of these carried a lower case and a pascal case
// property over the same backing field, because RestSharp's deserializer matched by exact
// name. System.Text.Json is configured case-insensitive (see JiraHttpClient.JsonOptions),
// which makes the pair a duplicate it would refuse to bind.

namespace Time.Tracking.Jira
{
    /// <summary>
    /// What Jira's own issue autocomplete answers. It comes back in sections — "hs" for the
    /// issues recently visited, "cs" for the search itself — and it is the only endpoint that
    /// matches half a key, which JQL cannot do.
    /// </summary>
    internal class IssuePickerResult
    {
        public List<IssuePickerSection> Sections { get; set; }
    }

    internal class IssuePickerSection
    {
        public string Id { get; set; }

        public List<IssuePickerIssue> Issues { get; set; }
    }

    internal class IssuePickerIssue
    {
        public string Key { get; set; }

        // The title comes twice: summaryText plain, summary with markup around the part
        // that matched. Only the plain one is any use in a combo box.
        public string SummaryText { get; set; }
    }
}
