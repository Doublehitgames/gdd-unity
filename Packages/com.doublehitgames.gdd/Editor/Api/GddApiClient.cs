using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Api
{
    /// <summary>Where the bearer token comes from, and how to renew it after a 401.</summary>
    internal interface ITokenSource
    {
        /// <summary>Null when nobody is signed in.</summary>
        Task<string> GetTokenAsync();

        /// <summary>True when a new token was obtained and the request is worth retrying.</summary>
        Task<bool> TryRefreshAsync();
    }

    internal enum GddErrorKind
    {
        /// <summary>No answer at all: offline, DNS, TLS.</summary>
        Network,
        /// <summary>Not signed in, or the token was revoked and could not be renewed.</summary>
        Unauthorized,
        /// <summary>Signed in, but this account cannot see that project.</summary>
        Forbidden,
        NotFound,
        RateLimited,
        Server,
    }

    internal sealed class GddApiException : Exception
    {
        public readonly GddErrorKind Kind;
        public readonly long Status;
        public readonly string Code;

        public GddApiException(GddErrorKind kind, string message, long status = 0, string code = null)
            : base(message)
        {
            Kind = kind;
            Status = status;
            Code = code;
        }
    }

    /// <summary>
    /// Read-side client of the GDD Manager REST API. Talks only to /api/v1 —
    /// never to the database behind it.
    /// </summary>
    internal sealed class GddApiClient
    {
        readonly string _baseUrl;
        readonly IHttpTransport _transport;
        readonly ITokenSource _tokens;

        public GddApiClient(string serverUrl, IHttpTransport transport, ITokenSource tokens)
        {
            _baseUrl = GddServer.Normalize(serverUrl);
            _transport = transport;
            _tokens = tokens;
        }

        public async Task<GddMe> GetMeAsync() =>
            Parse<MeEnvelope>(await GetAsync("/api/v1/me")).data;

        public async Task<GddProject[]> ListProjectsAsync() =>
            Parse<ProjectListEnvelope>(await GetAsync("/api/v1/projects")).data ?? Array.Empty<GddProject>();

        public async Task<GddSection[]> ListSectionsAsync(string projectId) =>
            Parse<SectionListEnvelope>(await GetAsync($"/api/v1/projects/{Uri.EscapeDataString(projectId)}/sections")).data
            ?? Array.Empty<GddSection>();

        public async Task<GddSection> GetSectionAsync(string projectId, string sectionId) =>
            Parse<SectionEnvelope>(await GetAsync(
                $"/api/v1/projects/{Uri.EscapeDataString(projectId)}/sections/{Uri.EscapeDataString(sectionId)}")).data;

        async Task<string> GetAsync(string path)
        {
            var token = await _tokens.GetTokenAsync();
            if (string.IsNullOrEmpty(token))
                throw new GddApiException(GddErrorKind.Unauthorized, "Not signed in to GDD Manager.");

            var response = await Send(path, token);
            if (response.Status == 401 && await _tokens.TryRefreshAsync())
                response = await Send(path, await _tokens.GetTokenAsync());

            if (response.IsSuccess) return response.Body;
            throw ToException(response);
        }

        Task<HttpResponse> Send(string path, string token)
        {
            var request = new HttpRequest { Url = _baseUrl + path };
            request.Headers["Authorization"] = "Bearer " + token;
            request.Headers["Accept"] = "application/json";
            return _transport.SendAsync(request);
        }

        static T Parse<T>(string body)
        {
            try
            {
                return JsonUtility.FromJson<T>(body);
            }
            catch (ArgumentException e)
            {
                throw new GddApiException(GddErrorKind.Server, "GDD Manager sent a response the plugin could not read: " + e.Message);
            }
        }

        internal static GddApiException ToException(HttpResponse response)
        {
            if (response.Status == 0)
                return new GddApiException(GddErrorKind.Network,
                    "Could not reach GDD Manager" + (string.IsNullOrEmpty(response.NetworkError) ? "." : ": " + response.NetworkError));

            ApiErrorBody error = null;
            try
            {
                if (!string.IsNullOrEmpty(response.Body)) error = JsonUtility.FromJson<ApiErrorBody>(response.Body);
            }
            catch (ArgumentException)
            {
                // Not JSON (a proxy's HTML page, say): fall back to the status alone.
            }

            var message = !string.IsNullOrEmpty(error?.error) ? error.error : $"GDD Manager answered HTTP {response.Status}.";
            var kind = response.Status switch
            {
                401 => GddErrorKind.Unauthorized,
                403 => GddErrorKind.Forbidden,
                404 => GddErrorKind.NotFound,
                429 => GddErrorKind.RateLimited,
                _ => GddErrorKind.Server,
            };
            return new GddApiException(kind, message, response.Status, error?.code);
        }
    }

    internal static class GddServer
    {
        public const string DefaultUrl = "https://gdd-app.vercel.app";

        public static string Normalize(string url)
        {
            url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();
            return url.TrimEnd('/');
        }

        public static string SectionPageUrl(string serverUrl, string projectId, string sectionId) =>
            $"{Normalize(serverUrl)}/projects/{Uri.EscapeDataString(projectId)}/sections/{Uri.EscapeDataString(sectionId)}";

        public static string ProjectPageUrl(string serverUrl, string projectId) =>
            $"{Normalize(serverUrl)}/projects/{Uri.EscapeDataString(projectId)}";
    }
}
