using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Api;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Auth
{
    [Serializable]
    internal sealed class TokenResponse
    {
        public string access_token;
        public string refresh_token;
        public long expires_in;
        public string scope;
    }

    [Serializable]
    sealed class RegisterRequest
    {
        public string client_name;
        public string client_uri;
        public string[] redirect_uris;
    }

    [Serializable]
    sealed class RegisterResponse
    {
        public string client_id;
    }

    /// <summary>OAuth error body (RFC 6749 §5.2) — a different shape from /api/v1 errors.</summary>
    [Serializable]
    sealed class OAuthErrorBody
    {
        public string error;
        public string error_description;
    }

    internal sealed class OAuthException : Exception
    {
        public readonly string Error;

        public OAuthException(string error, string description)
            : base(string.IsNullOrEmpty(description) ? error : description)
        {
            Error = error;
        }
    }

    /// <summary>
    /// The wire side of GDD Manager's OAuth 2.1: dynamic client registration, the
    /// authorize URL, and the token endpoint. Public client, PKCE, no secret.
    /// </summary>
    internal sealed class OAuthClient
    {
        public const string ClientName = "Unity Editor (GDD Manager plugin)";
        public const string ClientUri = "https://github.com/Doublehitgames/gdd-unity";
        public const string Scope = "gdd";

        readonly string _baseUrl;
        readonly IHttpTransport _transport;

        public OAuthClient(string serverUrl, IHttpTransport transport)
        {
            _baseUrl = GddServer.Normalize(serverUrl);
            _transport = transport;
        }

        public async Task<string> RegisterAsync(IEnumerable<string> redirectUris)
        {
            var body = JsonUtility.ToJson(new RegisterRequest
            {
                client_name = ClientName,
                client_uri = ClientUri,
                redirect_uris = redirectUris.ToArray(),
            });
            var response = await _transport.SendAsync(new HttpRequest
            {
                Method = "POST",
                Url = _baseUrl + "/api/oauth/register",
                Body = body,
                ContentType = "application/json",
            });
            return Read<RegisterResponse>(response).client_id;
        }

        public string AuthorizeUrl(string clientId, string redirectUri, string codeChallenge, string state) =>
            _baseUrl + "/oauth/authorize?" + Form(new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["scope"] = Scope,
            });

        public Task<TokenResponse> ExchangeCodeAsync(string clientId, string code, string codeVerifier, string redirectUri) =>
            PostToken(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
            });

        public Task<TokenResponse> RefreshAsync(string clientId, string refreshToken) =>
            PostToken(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["refresh_token"] = refreshToken,
            });

        async Task<TokenResponse> PostToken(Dictionary<string, string> fields)
        {
            var response = await _transport.SendAsync(new HttpRequest
            {
                Method = "POST",
                Url = _baseUrl + "/api/oauth/token",
                Body = Form(fields),
                ContentType = "application/x-www-form-urlencoded",
            });
            var tokens = Read<TokenResponse>(response);
            if (string.IsNullOrEmpty(tokens.access_token))
                throw new OAuthException("invalid_response", "GDD Manager did not return an access token.");
            return tokens;
        }

        static T Read<T>(HttpResponse response)
        {
            if (response.Status == 0)
                throw new GddApiException(GddErrorKind.Network, "Could not reach GDD Manager: " + response.NetworkError);

            if (!response.IsSuccess)
            {
                OAuthErrorBody error = null;
                try { error = JsonUtility.FromJson<OAuthErrorBody>(response.Body ?? ""); }
                catch (ArgumentException) { }
                throw new OAuthException(error?.error ?? "http_" + response.Status, error?.error_description);
            }
            return JsonUtility.FromJson<T>(response.Body);
        }

        internal static string Form(Dictionary<string, string> fields)
        {
            var sb = new StringBuilder();
            foreach (var field in fields)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(field.Key)).Append('=').Append(Uri.EscapeDataString(field.Value ?? ""));
            }
            return sb.ToString();
        }
    }
}
