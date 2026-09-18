using System.Collections.Generic;

namespace Time.Tracking.Jira
{
    internal enum HttpVerb
    {
        Get,
        Post
    }

    /// <summary>
    /// One call to Jira, built up before it is sent.
    /// </summary>
    /// <remarks>
    /// An interface so the request factory stays mockable, exactly as it was when this was
    /// RestSharp's IRestRequest. The method names are the ones the factory already used.
    /// </remarks>
    internal interface IJiraRequest
    {
        string Resource { get; }
        HttpVerb Verb { get; }

        void AddHeader(string name, string value);
        void AddQueryParameter(string name, string value);
        void AddJsonBody(object body);
    }

    internal sealed class JiraRequest : IJiraRequest
    {
        private readonly List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
        private readonly List<KeyValuePair<string, string>> query = new List<KeyValuePair<string, string>>();

        public JiraRequest(string resource, HttpVerb verb)
        {
            Resource = resource;
            Verb = verb;
        }

        public string Resource { get; private set; }

        public HttpVerb Verb { get; private set; }

        /// <summary>Null when there is no body, which is every GET.</summary>
        public object JsonBody { get; private set; }

        public IEnumerable<KeyValuePair<string, string>> Headers
        {
            get { return headers; }
        }

        public IEnumerable<KeyValuePair<string, string>> QueryParameters
        {
            get { return query; }
        }

        public void AddHeader(string name, string value)
        {
            headers.Add(new KeyValuePair<string, string>(name, value));
        }

        public void AddQueryParameter(string name, string value)
        {
            query.Add(new KeyValuePair<string, string>(name, value));
        }

        public void AddJsonBody(object body)
        {
            JsonBody = body;
        }
    }

    /// <summary>Builds requests, so the factory that uses it can be given a fake.</summary>
    internal interface IJiraRequestFactory
    {
        IJiraRequest Create(string resource, HttpVerb verb);
    }

    internal sealed class JiraRequestFactory : IJiraRequestFactory
    {
        public IJiraRequest Create(string resource, HttpVerb verb)
        {
            return new JiraRequest(resource, verb);
        }
    }
}
