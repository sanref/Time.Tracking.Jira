/**************************************************************************
Copyright 2016 Carsten Gehling

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
**************************************************************************/
using Time.Tracking.Jira.Logging;
using System;
using System.Net;

namespace Time.Tracking.Jira
{
    /// <summary>
    /// Jira refused a request, or never answered it. The reason travels with the exception:
    /// every call runs on a background task over one shared requester, so reading its
    /// <see cref="JiraApiRequester.ErrorMessage"/> afterwards could pick up another call's.
    /// </summary>
    internal class RequestDeniedException : Exception
    {
        public RequestDeniedException()
        {
        }

        public RequestDeniedException(string message, bool unreachable)
            : base(message)
        {
            Unreachable = unreachable;
        }

        /// <summary>
        /// Jira did not answer — no network, no VPN, a name that does not resolve, a timeout —
        /// or answered that it cannot right now (5xx, 429). Worth trying again later, unlike a
        /// refused login, which retrying will not change.
        /// </summary>
        public bool Unreachable { get; private set; }
    }


    internal class JiraApiRequester : IJiraApiRequester
    {
        public string ErrorMessage { get; private set; }

        public JiraApiRequester(IJiraHttpClientFactory httpClientFactory, IJiraApiRequestFactory jiraApiRequestFactory)
        {
            this.httpClientFactory = httpClientFactory;
            this.jiraApiRequestFactory = jiraApiRequestFactory;
            ErrorMessage = "";
        }


        public T DoAuthenticatedRequest<T>(IJiraRequest request)
            where T : new()
        {
            IJiraHttpClient client = httpClientFactory.Create();

			Uri uri = client.BuildUri(request);

			_logger.Log(string.Format("Request: {0}", uri));
            JiraResponse<T> response = client.Execute<T>(request);

            _logger.Log(string.Format("Response: {0} - {1}", response.StatusCode, StringHelpers.Truncate(response.Content ?? "", 2048)));

            if (response.ErrorException != null)
            {
                _logger.Log(string.Format("Response ErrorException: {0}", response.ErrorException.Message), response.ErrorException);
            }

            // If login session has expired, try to login, and then re-execute the original request
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.Log("Try to re-authenticate");
                JiraResponse refused;
                if (!ReAuthenticate(out refused))
                    throw Denied(refused);

                _logger.Log(string.Format("Authenticated. Resend request: {0}", client.BuildUri(request)));
                response = client.Execute<T>(request);
                _logger.Log(string.Format("Response after re-auth: {0} - {1}", response.StatusCode, StringHelpers.Truncate(response.Content ?? "", 2048)));
            }

            // Check for success status codes (2xx range)
            int statusCodeValue = (int)response.StatusCode;
            if (statusCodeValue < 200 || statusCodeValue >= 300)
            {
                _logger.Log(string.Format("Request failed with StatusCode: {0} ({1})", response.StatusCode, statusCodeValue));

                // Special handling for Gone status
                if (response.StatusCode == HttpStatusCode.Gone)
                {
                    ErrorMessage = string.Format("HTTP 410 Gone - The resource is no longer available. URL: {0}. This may indicate the API changed or the endpoint was deprecated.", uri);
                }
                else
                {
                    ErrorMessage = response.ErrorMessage ?? string.Format("HTTP {0} - {1}", statusCodeValue, response.StatusDescription);
                }

                throw Denied(response);
            }

            if (response.Data == null && !string.IsNullOrEmpty(response.Content))
            {
                _logger.Log(string.Format("WARNING: Response.Data is null but Content is not empty. Content-Type: {0}", response.ContentType ?? "null"));
            }

            ErrorMessage = "";
            return response.Data;
        }


        /// <summary>
        /// Logs in again. On failure <paramref name="refused"/> is Jira's answer to the login,
        /// or null when there were no credentials to try.
        /// </summary>
        protected bool ReAuthenticate(out JiraResponse refused)
        {
            refused = null;
            IJiraRequest request;

            try
            {
                request = jiraApiRequestFactory.CreateReAuthenticateRequest();
            }
            catch (AuthenticateNotYetCalledException)
            {
                ErrorMessage = "Not signed in to Jira";
                return false;
            }

            var client = httpClientFactory.Create(true);
            _logger.Log(string.Format("Request: {0}", client.BuildUri(request)));
            JiraResponse response = client.Execute(request);
            _logger.Log(string.Format("Response: {0} - {1}", response.StatusCode, StringHelpers.Truncate(response.Content, 2048)));

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ErrorMessage = "Invalid username or password";
                refused = response;
                return false;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                ErrorMessage = response.ErrorMessage ?? string.Format("HTTP {0} - {1}", (int)response.StatusCode, response.StatusDescription);
                refused = response;
                return false;
            }

            ErrorMessage = "";
            return true;
        }


        /// <summary>The exception for a failed request, carrying <see cref="ErrorMessage"/> as its reason.</summary>
        private RequestDeniedException Denied(JiraResponse response)
        {
            return new RequestDeniedException(ErrorMessage, IsUnreachable(response));
        }


        /// <summary>
        /// Jira never answered, or answered that it cannot serve anything right now. Null —
        /// a login that was never attempted — is neither.
        /// </summary>
        internal static bool IsUnreachable(JiraResponse response)
        {
            if (response == null)
                return false;

            // Only a transport failure fills this: no network, DNS, TLS, timeout
            if (response.ErrorException != null)
                return true;

            int status = (int)response.StatusCode;
            return status >= 500 || status == 429;
        }


        private Logger _logger = Logger.Instance;

        private IJiraHttpClientFactory httpClientFactory;
        private IJiraApiRequestFactory jiraApiRequestFactory;
    }
}
