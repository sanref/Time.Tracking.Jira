using System;
using System.Net;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// What Jira answered.
    /// </summary>
    /// <remarks>
    /// A failed call is reported here, not thrown: a 401 is the normal way a session expires
    /// and <see cref="JiraApiRequester"/> re-authenticates on it. Only a transport failure —
    /// no network, bad certificate, refused connection — lands in <see cref="ErrorException"/>,
    /// and it is not thrown either, for the same reason.
    /// </remarks>
    internal class JiraResponse
    {
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>The reason phrase, or the transport failure when there was no response.</summary>
        public string StatusDescription { get; set; }

        private string content = "";

        /// <summary>
        /// Never null, so the logging and truncation around it cannot fault on a response
        /// that carried no body. RestSharp guaranteed the same thing.
        /// </summary>
        public string Content
        {
            get { return content; }
            set { content = value ?? ""; }
        }

        public string ContentType { get; set; }

        /// <summary>Set when the call never reached Jira. Null on any answer, including a 500.</summary>
        public Exception ErrorException { get; set; }

        /// <summary>Null unless something went wrong, so it can stand in for a status line.</summary>
        public string ErrorMessage { get; set; }
    }

    internal sealed class JiraResponse<T> : JiraResponse
    {
        /// <summary>
        /// The deserialized body, or default(T) when there was no body, it did not parse, or
        /// the call failed. A null here with a non-empty <see cref="JiraResponse.Content"/> is
        /// what the requester logs a warning about.
        /// </summary>
        public T Data { get; set; }
    }
}
