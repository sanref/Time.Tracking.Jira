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
using System;
using System.Collections.Generic;

namespace Time.Tracking.Jira
{
    internal class JiraClient
    {
        public bool SessionValid { get; private set; }

        /// <summary>
        /// Why the last call that failed did. Shared by every call, so for the log only — the
        /// one the user reads, a failed worklog, comes back from <see cref="PostWorklog"/> itself.
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// The last <see cref="Authenticate"/> or <see cref="ValidateSession"/> failed because
        /// Jira could not be reached, not because it refused: worth retrying once the network
        /// is back. See <see cref="RequestDeniedException.Unreachable"/>.
        /// </summary>
        public bool Unreachable { get; private set; }

        #region public methods
        public JiraClient(IJiraApiRequestFactory jiraApiRequestFactory, IJiraApiRequester jiraApiRequester)
        {
            this.jiraApiRequestFactory = jiraApiRequestFactory;
            this.jiraApiRequester = jiraApiRequester;

            SessionValid = false;
            ErrorMessage = "";
        }


        public bool Authenticate(string username, string password)
        {
            SessionValid = false;
            Unreachable = false;

            var request = jiraApiRequestFactory.CreateAuthenticateRequest(username, password);
            try
            {
                jiraApiRequester.DoAuthenticatedRequest<object>(request);
                JiraTimeHelpers.Configuration = GetTimeTrackingConfiguration();
                return true;
            }
            catch (RequestDeniedException ex)
            {
                ErrorMessage = ex.Message;
                Unreachable = ex.Unreachable;
                return false;
            }
        }


        public bool ValidateSession()
        {
            SessionValid = false;
            Unreachable = false;

            var request = jiraApiRequestFactory.CreateValidateSessionRequest();
            try
            {
                jiraApiRequester.DoAuthenticatedRequest<object>(request);
                SessionValid = true;
                return true;
            }
            catch (RequestDeniedException ex)
            {
                ErrorMessage = ex.Message;
                Unreachable = ex.Unreachable;
                return false;
            }
        }


        public SearchResult GetIssuesByJQL(string jql)
        {
            var request = jiraApiRequestFactory.CreateGetIssuesByJQLRequest(jql);
            try
            {
                return jiraApiRequester.DoAuthenticatedRequest<SearchResult>(request);
            }
            catch (RequestDeniedException ex)
            {
                ErrorMessage = ex.Message;
                return null;
            }
        }


        public IssuePickerResult GetIssuePickerSuggestions(string query)
        {
            var request = jiraApiRequestFactory.CreateGetIssuePickerRequest(query);
            try
            {
                return jiraApiRequester.DoAuthenticatedRequest<IssuePickerResult>(request);
            }
            catch (RequestDeniedException ex)
            {
                ErrorMessage = ex.Message;
                return null;
            }
        }


        public string GetIssueSummary(string key)
        {
            var request = jiraApiRequestFactory.CreateGetIssueSummaryRequest(key);
            var issue = jiraApiRequester.DoAuthenticatedRequest<Issue>(request).Fields;
            string ret;
            try
            {
                ret = issue.Summary;
            }
            catch
            {
                ret = "";
            }
            return ret;
        }

        public TimeTrackingConfiguration GetTimeTrackingConfiguration()
        {
            var request = jiraApiRequestFactory.CreateGetConfigurationRequest();
            try
            {
                return jiraApiRequester.DoAuthenticatedRequest<JiraConfiguration>(request).timeTrackingConfiguration;
            }
            catch (RequestDeniedException ex)
            {
                ErrorMessage = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Posts the worklog. On failure <paramref name="error"/> is why — Jira's own words
        /// when it gave any — to show the user as is; null on success.
        /// </summary>
        public bool PostWorklog(string key, DateTimeOffset startTime, TimeSpan time, string comment, EstimateUpdateMethods estimateUpdateMethod, string estimateUpdateValue, out string error)
        {
            error = null;

            var request = jiraApiRequestFactory.CreatePostWorklogRequest(key, startTime, time, comment, estimateUpdateMethod, estimateUpdateValue);
            try
            {
                jiraApiRequester.DoAuthenticatedRequest<object>(request);
                return true;
            }
            catch (RequestDeniedException ex)
            {
                error = ex.Message;
                ErrorMessage = ex.Message;
                return false;
            }
        }


        #endregion

        #region private members
        private IJiraApiRequestFactory jiraApiRequestFactory;
        private IJiraApiRequester jiraApiRequester;
        #endregion
    }
}
