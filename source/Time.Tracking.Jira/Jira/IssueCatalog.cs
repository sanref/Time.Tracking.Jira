using Time.Tracking.Jira.Logging;
using Time.Tracking.Jira.Wpf.ViewModels;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Finding issues in Jira, and remembering the titles that persistence does not carry.
    /// </summary>
    /// <remarks>
    /// Every method here blocks or is pure: no collections that a view binds to, no
    /// dispatcher. The caller owns the thread and the notification. That is what makes the
    /// search strategy and the summary cache reachable from a test.
    /// </remarks>
    internal sealed class IssueCatalog
    {
        private readonly JiraSession session;

        // Parent titles are not persisted, so they are resolved lazily. The two sets stop a
        // key that Jira cannot resolve from being asked for again on every refresh.
        private readonly Dictionary<string, string> summaries = new Dictionary<string, string>();
        private readonly HashSet<string> pending = new HashSet<string>();
        private readonly HashSet<string> unresolved = new HashSet<string>();

        public IssueCatalog(JiraSession session)
        {
            this.session = session;
        }

        #region search

        /// <summary>
        /// The JQL for a whole issue key, null for anything else. Nothing else belongs in
        /// JQL: a key cannot be matched by halves there, and titles are not what the pickers
        /// search on.
        /// </summary>
        public static string KeyJql(string text)
        {
            string query = (text ?? "").Trim();

            // The same shape of key the rest of the app recognises, anchored
            if (!Regex.IsMatch(query, @"^[A-Za-z0-9_]+-\d+$"))
                return null;

            return string.Format("key = {0}", query.ToUpperInvariant());
        }

        /// <summary>
        /// Blocks. A whole key is asked for by key, which answers for any issue in Jira —
        /// closed, someone else's, in a project you have never touched. Half a key has to go
        /// to Jira's own autocomplete instead, because JQL has no way to match one.
        /// </summary>
        public List<IssueItem> Search(string query)
        {
            List<IssueItem> found = new List<IssueItem>();
            string jql = KeyJql(query);

            try
            {
                if (jql != null)
                {
                    SearchResult result = session.Client.GetIssuesByJQL(jql);

                    // A key that does not exist is a JQL error, not an empty result: the
                    // half-typed key on the way to a real one lands here every time.
                    if (result == null)
                        Logger.Instance.Log(string.Format("Issue search [{0}] returned nothing: {1}", jql,
                            session.ErrorMessage ?? "No error message available"));
                    else if (result.Issues != null)
                        foreach (Issue issue in result.Issues)
                            found.Add(new IssueItem(issue.Key, issue.Fields == null ? "" : (issue.Fields.Summary ?? "")));
                }
                else
                {
                    IssuePickerResult suggestions = session.Client.GetIssuePickerSuggestions(query);

                    if (suggestions == null || suggestions.Sections == null)
                        Logger.Instance.Log(string.Format("Issue picker returned nothing for [{0}]: {1}", query,
                            session.ErrorMessage ?? "No error message available"));
                    else
                        foreach (IssuePickerSection section in suggestions.Sections)
                            if (section != null && section.Issues != null)
                                foreach (IssuePickerIssue issue in section.Issues)
                                    found.Add(new IssueItem(issue.Key, issue.SummaryText));
                }

                Logger.Instance.Log(string.Format("Issue search for [{0}]: {1} suggestion(s)", query, found.Count));
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Error searching issues for [{0}]: {1}", query, ex.Message));
            }

            return found;
        }

        /// <summary>
        /// Blocks. The subtasks of one parent, ordered by key. <paramref name="error"/> is
        /// null when the query succeeded, whether or not it found anything.
        /// </summary>
        public List<IssueItem> Subtasks(string parentKey, out string error)
        {
            List<IssueItem> subtasks = new List<IssueItem>();
            error = null;

            try
            {
                SearchResult result = session.Client.GetIssuesByJQL(
                    string.Format("parent = {0} ORDER BY key ASC", parentKey));

                if (result == null)
                    error = session.ErrorMessage ?? "No error message available";
                else if (result.Issues != null)
                    foreach (Issue issue in result.Issues)
                        subtasks.Add(new IssueItem(issue.Key, issue.Fields == null ? "" : (issue.Fields.Summary ?? "")));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null)
                Logger.Instance.Log(string.Format("Error loading subtasks for {0}: {1}", parentKey, error));

            return subtasks;
        }

        /// <summary>Blocks. The issue title, or null when Jira would not give one up.</summary>
        public string FetchSummary(string key)
        {
            try
            {
                return session.Client.GetIssueSummary(key);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Error loading summary for {0}: {1}", key, ex.Message));
                return null;
            }
        }

        #endregion

        #region summary cache

        /// <summary>The title known for this key, or "" when there is none yet.</summary>
        public string Lookup(string key)
        {
            string summary;
            if (!string.IsNullOrEmpty(key) && summaries.TryGetValue(key, out summary))
                return summary ?? "";

            return "";
        }

        public void Remember(string key, string summary)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(summary))
                return;

            summaries[key] = summary;
            unresolved.Remove(key);
        }

        /// <summary>
        /// Whether this key is worth asking Jira about: not already in flight, and not one
        /// Jira has already refused. Without the second check a redraw would ask forever.
        /// </summary>
        public bool ShouldRequest(string key)
        {
            return !pending.Contains(key) && !unresolved.Contains(key);
        }

        public void MarkPending(string key)
        {
            pending.Add(key);
        }

        public void MarkUnresolved(string key)
        {
            pending.Remove(key);
            unresolved.Add(key);
        }

        public void ClearPending(string key)
        {
            pending.Remove(key);
        }

        /// <summary>A refresh is an explicit "try again", so refused keys get another go.</summary>
        public void ForgetUnresolved()
        {
            unresolved.Clear();
        }

        #endregion
    }
}
