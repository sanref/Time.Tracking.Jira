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
using System.Web;

namespace Time.Tracking.Jira
{
	internal class AuthenticateNotYetCalledException : Exception
	{
	}

	internal class JiraApiRequestFactory : IJiraApiRequestFactory
	{
		#region public methods
		public JiraApiRequestFactory(IJiraRequestFactory requestFactory)
		{
			this.requestFactory = requestFactory;
			this.username = "";
			this.password = "";
		}


		public IJiraRequest CreateValidateSessionRequest()
		{
			var request = requestFactory.Create("/rest/auth/1/session", HttpVerb.Get);
			AddAuthHeader(request);
			return request;
		}


		public IJiraRequest CreateGetIssuesByJQLRequest(string jql)
		{            
			// Switched to API v3 as per Jira error message:
			// "The requested API has been removed. Please migrate to the /rest/api/3/search/jql API"
			// Note: Per migration guide, endpoint changed from /rest/api/2/search to /rest/api/3/search/jql
			// Adding the 'fields' parameter to specify which fields we want in the response
			var request = requestFactory.Create(String.Format("/rest/api/3/search/jql?jql={0}&fields=key,summary,timetracking,project,parent&maxResults=200", HttpUtility.UrlEncode(jql)), HttpVerb.Get);
			AddAuthHeader(request);
			return request;
		}


		public IJiraRequest CreateGetIssuePickerRequest(string query)
		{
			// Jira's own autocomplete. Unlike JQL it matches part of a key, and it searches
			// everything the user is allowed to see rather than a filter we spell out here.
			// The empty currentJQL is what Jira's own UI sends when the picker is not scoped
			// to a board or a filter: it is what makes the search cover every issue.
			var request = requestFactory.Create(String.Format("/rest/api/3/issue/picker?query={0}&currentJQL=&showSubTasks=true&showSubTaskParent=true", HttpUtility.UrlEncode(query)), HttpVerb.Get);
			AddAuthHeader(request);
			return request;
		}


		public IJiraRequest CreateGetIssueSummaryRequest(string key)
		{
			var request = requestFactory.Create(String.Format("/rest/api/2/issue/{0}", key.Trim()), HttpVerb.Get);
			AddAuthHeader(request);
			return request;
		}

		public IJiraRequest CreatePostWorklogRequest(string key, DateTimeOffset started, TimeSpan time, string comment, EstimateUpdateMethods adjustmentMethod, string adjustmentValue)
		{
			var request = requestFactory.Create(String.Format("/rest/api/2/issue/{0}/worklog", key.Trim()), HttpVerb.Post);
			request.AddJsonBody(new
				{
					timeSpent = JiraTimeHelpers.TimeSpanToJiraTime(time),
					started = JiraTimeHelpers.DateTimeToJiraDateTime(started),
					comment = comment
				}
			);
			switch(adjustmentMethod) {
				case EstimateUpdateMethods.Leave:
					request.AddQueryParameter("adjustEstimate", "leave");
					break;
				case EstimateUpdateMethods.SetTo:
					request.AddQueryParameter("adjustEstimate", "new");
					request.AddQueryParameter("newEstimate", adjustmentValue);
					break;
				case EstimateUpdateMethods.ManualDecrease:
					request.AddQueryParameter("adjustEstimate", "manual");
					request.AddQueryParameter("reduceBy", adjustmentValue);
					break;
				case EstimateUpdateMethods.Auto:
					request.AddQueryParameter("adjustEstimate", "auto");
					break;
			}
			AddAuthHeader(request);
			return request;
		}

		public IJiraRequest CreateGetConfigurationRequest()
		{
			var request = requestFactory.Create("/rest/api/2/configuration", HttpVerb.Get);
			AddAuthHeader(request);
			return request;
		}


		public IJiraRequest CreateAuthenticateRequest(string username, string password)
		{
			this.username = username;
			this.password = password;

			var request = requestFactory.Create("/rest/auth/1/session", HttpVerb.Get);
			AddAuthHeader(request);
			return request;
		}

		private void AddAuthHeader(IJiraRequest request)
		{
			request.AddHeader("Authorization", "Basic " + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{this.username}:{this.password}")));
		}

		public IJiraRequest CreateReAuthenticateRequest()
		{
			if (string.IsNullOrEmpty(this.username))
				throw new AuthenticateNotYetCalledException();

			return CreateAuthenticateRequest(this.username, this.password);
		}
		#endregion


		#region private members
		private IJiraRequestFactory requestFactory;

		private string username;
		private string password;
		#endregion
	}
}
