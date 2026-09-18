using System;

namespace Time.Tracking.Jira.Wpf.ViewModels
{
    /// <summary>A Jira issue as offered in a picker: the key is the value, the summary is context.</summary>
    public class IssueItem
    {
        public IssueItem(string key, string summary)
        {
            Key = key ?? "";
            Summary = summary ?? "";
        }

        public string Key { get; private set; }

        public string Summary { get; private set; }

        /// <summary>
        /// Whether this issue answers to a search. Only the key is looked at — the pickers
        /// search by issue code, and matching titles as well brought back issues that looked
        /// unrelated to what had been typed.
        /// </summary>
        public bool Matches(string[] terms)
        {
            foreach (string term in terms)
                if (Key.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) < 0)
                    return false;

            return true;
        }

        /// <summary>
        /// The words of a search, all of which have to match. Shared so that what the combo
        /// filters on and what a Jira search is allowed to bring back stay the same rule.
        /// </summary>
        public static string[] Terms(string text)
        {
            return (text ?? "").Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>The combo boxes are editable, so their text must round-trip to the key.</summary>
        public override string ToString()
        {
            return Key;
        }
    }
}
