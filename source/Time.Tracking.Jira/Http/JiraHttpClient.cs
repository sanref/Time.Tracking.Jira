using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Time.Tracking.Jira
{
    /// <summary>Sends a <see cref="IJiraRequest"/> and reports what came back.</summary>
    internal interface IJiraHttpClient
    {
        /// <summary>The absolute url a request will be sent to. For the log.</summary>
        Uri BuildUri(IJiraRequest request);

        JiraResponse<T> Execute<T>(IJiraRequest request) where T : new();

        JiraResponse Execute(IJiraRequest request);
    }

    /// <summary>
    /// The Jira transport, over <see cref="HttpClient"/> and System.Text.Json.
    /// </summary>
    /// <remarks>
    /// Blocking on purpose. Every caller already runs on a background task and the rest of
    /// the app is synchronous, so waiting here keeps the change to the transport instead of
    /// spreading async through every call site. There is no SynchronizationContext on those
    /// threads, so the wait cannot deadlock.
    /// </remarks>
    internal sealed class JiraHttpClient : IJiraHttpClient
    {
        /// <summary>
        /// Case-insensitive because Jira answers in camelCase while most of these DTOs are
        /// PascalCase, and a couple — the time tracking configuration — are not.
        /// </summary>
        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient http;
        private readonly string baseUrl;

        public JiraHttpClient(HttpClient http, string baseUrl)
        {
            this.http = http;
            this.baseUrl = (baseUrl ?? "").TrimEnd('/');
        }

        public Uri BuildUri(IJiraRequest request)
        {
            StringBuilder url = new StringBuilder();
            url.Append(baseUrl);

            string resource = request.Resource ?? "";
            if (resource.Length > 0 && !resource.StartsWith("/"))
                url.Append('/');
            url.Append(resource);

            JiraRequest concrete = request as JiraRequest;
            if (concrete != null)
            {
                // Some resources already carry their own query string, so the first extra
                // parameter has to know whether it opens one or joins it.
                bool first = resource.IndexOf('?') < 0;
                foreach (KeyValuePair<string, string> parameter in concrete.QueryParameters)
                {
                    url.Append(first ? '?' : '&');
                    first = false;
                    url.Append(Uri.EscapeDataString(parameter.Key));
                    url.Append('=');
                    url.Append(Uri.EscapeDataString(parameter.Value ?? ""));
                }
            }

            return new Uri(url.ToString());
        }

        public JiraResponse Execute(IJiraRequest request)
        {
            JiraResponse response = new JiraResponse();
            Send(request, response);
            return response;
        }

        public JiraResponse<T> Execute<T>(IJiraRequest request) where T : new()
        {
            JiraResponse<T> response = new JiraResponse<T>();
            Send(request, response);

            // A body that does not parse is left as null rather than thrown: the requester
            // already logs that case, and a status code tells the caller more than a
            // deserialization error would.
            if (response.ErrorException == null && !string.IsNullOrEmpty(response.Content))
            {
                try
                {
                    response.Data = JsonSerializer.Deserialize<T>(response.Content, JsonOptions);
                }
                catch (JsonException)
                {
                }
            }

            return response;
        }

        /// <summary>Does the call and fills everything except the deserialized body.</summary>
        private void Send(IJiraRequest request, JiraResponse response)
        {
            try
            {
                using (HttpRequestMessage message = BuildMessage(request))
                using (HttpResponseMessage answer = http.Send(message))
                {
                    response.StatusCode = answer.StatusCode;
                    response.StatusDescription = answer.ReasonPhrase;
                    response.Content = answer.Content == null
                        ? ""
                        : answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    response.ContentType = answer.Content == null || answer.Content.Headers.ContentType == null
                        ? null
                        : answer.Content.Headers.ContentType.ToString();

                    if (!answer.IsSuccessStatusCode)
                        response.ErrorMessage = DescribeFailure(answer.StatusCode, answer.ReasonPhrase, response.Content);
                }
            }
            catch (Exception ex)
            {
                // Never reached Jira. Reported rather than thrown, so the requester's status
                // handling stays the one place that decides what a failure means.
                response.ErrorException = ex;
                response.ErrorMessage = ex.Message;
                response.StatusDescription = ex.Message;
                response.StatusCode = 0;
            }
        }

        /// <summary>
        /// The line a refused request is reported with. Jira's own words when the body has
        /// them, with the status kept alongside for the log; the bare status line otherwise.
        /// </summary>
        internal static string DescribeFailure(HttpStatusCode status, string reasonPhrase, string content)
        {
            string jira = JiraErrorText.Describe(content);

            return jira != null
                ? string.Format("{0} (HTTP {1})", jira, (int)status)
                : string.Format("HTTP {0} - {1}", (int)status, reasonPhrase);
        }

        private HttpRequestMessage BuildMessage(IJiraRequest request)
        {
            HttpRequestMessage message = new HttpRequestMessage(
                request.Verb == HttpVerb.Post ? HttpMethod.Post : HttpMethod.Get,
                BuildUri(request));

            JiraRequest concrete = request as JiraRequest;
            if (concrete == null)
                return message;

            foreach (KeyValuePair<string, string> header in concrete.Headers)
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (concrete.JsonBody != null)
                message.Content = new StringContent(
                    JsonSerializer.Serialize(concrete.JsonBody, JsonOptions),
                    Encoding.UTF8, "application/json");

            return message;
        }

    }

    /// <summary>Hands out clients, and drops the session cookies when asked.</summary>
    internal interface IJiraHttpClientFactory
    {
        string BaseUrl { get; set; }

        IJiraHttpClient Create(bool invalidateCookies = false);
    }

    internal sealed class JiraHttpClientFactory : IJiraHttpClientFactory, IDisposable
    {
        private readonly object gate = new object();
        private HttpClient http;

        public string BaseUrl { get; set; }

        public JiraHttpClientFactory()
        {
            BaseUrl = "";
        }

        public IJiraHttpClient Create(bool invalidateCookies = false)
        {
            lock (gate)
            {
                // A re-authentication has to start from a clean cookie jar, and the jar
                // belongs to the handler, so dropping it means a new HttpClient.
                if (invalidateCookies || http == null)
                {
                    HttpClient previous = http;
                    http = NewClient();
                    if (previous != null)
                        previous.Dispose();
                }

                return new JiraHttpClient(http, BaseUrl);
            }
        }

        private static HttpClient NewClient()
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            };

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(100)
            };
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (http != null)
                {
                    http.Dispose();
                    http = null;
                }
            }
        }
    }
}
