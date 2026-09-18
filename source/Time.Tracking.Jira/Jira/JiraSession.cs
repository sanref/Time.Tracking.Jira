using Time.Tracking.Jira.Logging;
using System;

namespace Time.Tracking.Jira
{
    /// <summary>How a connection attempt ended.</summary>
    internal enum SessionState
    {
        Connected,
        AuthenticationFailed,
        SessionInvalid,

        /// <summary>
        /// Jira did not answer: no network, VPN down, a timeout, a 5xx. The only outcome worth
        /// retrying on its own — a refused login stays refused, and repeating it against
        /// Atlassian can end in a CAPTCHA or a locked account.
        /// </summary>
        Unreachable
    }

    /// <summary>The outcome of <see cref="JiraSession.Connect"/>, for the caller to render.</summary>
    internal sealed class SessionResult
    {
        public SessionState State { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Owns the Jira stack and the connect sequence.
    /// </summary>
    /// <remarks>
    /// The whole stack used to be assembled inside LedgerViewModel's constructor, which left
    /// the view model impossible to build without one, and the connect sequence interleaved
    /// with dispatcher marshalling. Connect blocks and returns a result; whoever calls it
    /// decides which thread it runs on and what the user is shown.
    /// </remarks>
    internal sealed class JiraSession
    {
        private readonly JiraClient client;
        private readonly JiraHttpClientFactory clientFactory;

        // Two connects can overlap — Settings accepted while the startup one is still out, a
        // retry meeting a click on the status — and both write the same client. One at a time,
        // so the session ends up as the later one left it.
        private readonly object connectGate = new object();

        public JiraSession(string baseUrl)
        {
            JiraRequestFactory httpRequestFactory = new JiraRequestFactory();
            JiraApiRequestFactory requestFactory = new JiraApiRequestFactory(httpRequestFactory);

            clientFactory = new JiraHttpClientFactory();
            clientFactory.BaseUrl = baseUrl;

            JiraApiRequester requester = new JiraApiRequester(clientFactory, requestFactory);
            client = new JiraClient(requestFactory, requester);
        }

        /// <summary>For tests: a session over a client whose transport is a mock.</summary>
        internal JiraSession(JiraClient client)
        {
            this.client = client;
        }

        /// <summary>The Jira calls themselves. The session owns the connection, not the queries.</summary>
        public JiraClient Client
        {
            get { return client; }
        }

        public bool SessionValid
        {
            get { return client.SessionValid; }
        }

        public string ErrorMessage
        {
            get { return client.ErrorMessage; }
        }

        /// <summary>Follows the base url from Settings. No-op on a test session.</summary>
        public string BaseUrl
        {
            get { return clientFactory == null ? "" : clientFactory.BaseUrl; }
            set { if (clientFactory != null) clientFactory.BaseUrl = value; }
        }

        /// <summary>
        /// Authenticates, validates and loads the time tracking configuration. Blocks: the
        /// caller puts it on a background task.
        /// </summary>
        public SessionResult Connect(string username, string password)
        {
            lock (connectGate)
            {
                return ConnectOnce(username, password);
            }
        }

        private SessionResult ConnectOnce(string username, string password)
        {
            if (!client.Authenticate(username, password))
            {
                string error = client.ErrorMessage;
                if (!string.IsNullOrEmpty(error))
                    Logger.Instance.Log(string.Format("Connection error to Jira: {0}", error));

                return new SessionResult
                {
                    State = client.Unreachable ? SessionState.Unreachable : SessionState.AuthenticationFailed,
                    ErrorMessage = error
                };
            }

            if (!client.ValidateSession())
            {
                return new SessionResult
                {
                    State = client.Unreachable ? SessionState.Unreachable : SessionState.SessionInvalid,
                    ErrorMessage = client.ErrorMessage
                };
            }

            LoadTimeTrackingConfiguration();

            return new SessionResult { State = SessionState.Connected };
        }

        /// <summary>
        /// Jira's working day drives every time string the app writes. A failure here is not
        /// worth failing the connection over: JiraTimeHelpers falls back to 24h days.
        /// </summary>
        private void LoadTimeTrackingConfiguration()
        {
            try
            {
                TimeTrackingConfiguration configuration = client.GetTimeTrackingConfiguration();
                if (configuration != null)
                {
                    JiraTimeHelpers.Configuration = configuration;
                    Logger.Instance.Log(string.Format("Jira time tracking configuration loaded: {0}h per day",
                        configuration.workingHoursPerDay));
                }
                else
                {
                    Logger.Instance.Log("Could not load Jira time tracking configuration, using default 24h days");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(string.Format("Error loading Jira time tracking configuration: {0}", ex.Message));
            }
        }
    }
}
