using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// What Jira says went wrong, read from the body of a response it refused.
    /// </summary>
    /// <remarks>
    /// Jira explains a refusal in the body, not in the status line. A worklog it will not take
    /// comes back as <c>{"errorMessages":[...],"errors":{"field":"..."}}</c>, and that body is
    /// the only place that says the issue does not exist or that the user may not log work on
    /// it. The status line alone — "HTTP 400 - Bad Request" — gives the user nothing to act on.
    /// </remarks>
    internal static class JiraErrorText
    {
        /// <summary>
        /// The messages in a Jira error body, one per line, or null when the body is not JSON
        /// or carries none — an HTML error page from a proxy, say.
        /// </summary>
        public static string Describe(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            List<string> messages = new List<string>();

            try
            {
                using (JsonDocument document = JsonDocument.Parse(content))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return null;

                    JsonElement element;

                    if (root.TryGetProperty("errorMessages", out element) && element.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement message in element.EnumerateArray())
                            Add(messages, message);

                    // Per-field errors: the field name is Jira's, the message is for the user
                    if (root.TryGetProperty("errors", out element) && element.ValueKind == JsonValueKind.Object)
                        foreach (JsonProperty field in element.EnumerateObject())
                            Add(messages, field.Value);

                    // The shapes some endpoints use instead
                    if (root.TryGetProperty("message", out element))
                        Add(messages, element);

                    if (root.TryGetProperty("errorMessage", out element))
                        Add(messages, element);
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
        }

        private static void Add(List<string> messages, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String)
                return;

            string text = (value.GetString() ?? "").Trim();
            if (text.Length > 0 && !messages.Contains(text))
                messages.Add(text);
        }
    }
}
