using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Api;
using UnityEditor;
using UnityEngine;

namespace Doublehitgames.Gdd.Editor.Auth
{
    internal enum CredentialSource
    {
        None,
        /// <summary>Signed in through the browser.</summary>
        OAuth,
        /// <summary>A gdd_sk_ key pasted by the user.</summary>
        ApiKey,
        /// <summary>GDD_API_KEY from the environment — the route for CI and batchmode.</summary>
        Environment,
    }

    [Serializable]
    internal sealed class StoredCredentials
    {
        public string kind;
        public string accessToken;
        public string refreshToken;
        public long expiresAtUnix;
        public string clientId;
    }

    [Serializable]
    internal sealed class StoredOAuthClient
    {
        public string clientId;
        public int[] ports;
    }

    /// <summary>Per-person storage. Never a project file: credentials must not reach version control.</summary>
    internal interface ICredentialStore
    {
        string Get(string key);
        void Set(string key, string value);
        void Delete(string key);
    }

    /// <summary>
    /// EditorPrefs is per machine and shared by every Unity project on it, so one
    /// sign-in serves all of this person's projects.
    /// </summary>
    internal sealed class EditorPrefsCredentialStore : ICredentialStore
    {
        public string Get(string key) => EditorPrefs.GetString(key, null);
        public void Set(string key, string value) => EditorPrefs.SetString(key, value);
        public void Delete(string key) => EditorPrefs.DeleteKey(key);
    }

    /// <summary>
    /// Who the plugin talks to GDD Manager as: browser sign-in (OAuth), a pasted
    /// API key, or GDD_API_KEY from the environment, in that order.
    /// </summary>
    internal sealed class GddAuth : ITokenSource
    {
        /// <summary>
        /// GDD Manager matches redirect URIs exactly, so the client is registered
        /// once with all of these and a sign-in uses the first one that is free.
        /// </summary>
        internal static readonly int[] LoopbackPorts = { 47811, 47812, 47813, 47814, 47815 };

        public const string EnvironmentVariable = "GDD_API_KEY";
        const string KindOAuth = "oauth";
        const string KindApiKey = "apiKey";
        const string ApiKeyPrefix = "gdd_sk_";
        static readonly TimeSpan RefreshAhead = TimeSpan.FromMinutes(5);

        readonly ICredentialStore _store;
        readonly OAuthClient _oauth;
        readonly Func<DateTimeOffset> _now;
        readonly Func<string, string> _env;
        readonly string _credentialsKey;
        readonly string _clientKey;
        Task<bool> _refreshing;

        public GddAuth(string serverUrl, ICredentialStore store, IHttpTransport transport,
            Func<DateTimeOffset> now = null, Func<string, string> env = null)
        {
            var server = GddServer.Normalize(serverUrl);
            _store = store;
            _oauth = new OAuthClient(server, transport);
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _env = env ?? Environment.GetEnvironmentVariable;
            _credentialsKey = $"Doublehitgames.Gdd/{server}/credentials";
            _clientKey = $"Doublehitgames.Gdd/{server}/oauthClient";
        }

        public CredentialSource Source
        {
            get
            {
                var stored = Load();
                if (stored?.kind == KindOAuth) return CredentialSource.OAuth;
                if (stored?.kind == KindApiKey) return CredentialSource.ApiKey;
                return string.IsNullOrEmpty(_env(EnvironmentVariable)) ? CredentialSource.None : CredentialSource.Environment;
            }
        }

        public async Task<string> GetTokenAsync()
        {
            var stored = Load();
            if (stored == null) return NullIfEmpty(_env(EnvironmentVariable));
            if (stored.kind != KindOAuth) return stored.accessToken;

            if (DateTimeOffset.FromUnixTimeSeconds(stored.expiresAtUnix) - _now() < RefreshAhead)
            {
                await TryRefreshAsync();
                stored = Load();
            }
            return stored?.accessToken;
        }

        public Task<bool> TryRefreshAsync()
        {
            // Refresh tokens rotate: two refreshes with the same token and the
            // second one fails. Every caller shares the one in flight.
            if (_refreshing != null) return _refreshing;
            var refresh = RefreshOnce();
            // Only an unfinished refresh is shared: a finished one already cleared the field.
            if (!refresh.IsCompleted) _refreshing = refresh;
            return refresh;
        }

        async Task<bool> RefreshOnce()
        {
            try
            {
                var stored = Load();
                if (stored?.kind != KindOAuth || string.IsNullOrEmpty(stored.refreshToken)) return false;
                try
                {
                    Save(await _oauth.RefreshAsync(stored.clientId, stored.refreshToken), stored.clientId);
                    return true;
                }
                catch (OAuthException)
                {
                    // Another editor on this machine may have rotated the token first;
                    // then what is stored now is newer than what this one sent.
                    var now = Load();
                    if (now != null && now.refreshToken != stored.refreshToken) return true;
                    SignOut();
                    return false;
                }
            }
            finally
            {
                _refreshing = null;
            }
        }

        public void UseApiKey(string key)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key) || !key.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
                throw new ArgumentException($"A GDD Manager API key starts with {ApiKeyPrefix}.");
            Store(new StoredCredentials { kind = KindApiKey, accessToken = key });
        }

        public void SignOut() => _store.Delete(_credentialsKey);

        /// <summary>
        /// Opens the consent page and waits for the browser to come back. The
        /// caller passes how to open a URL so this stays testable.
        /// </summary>
        public async Task SignInWithBrowserAsync(Action<string> openUrl, CancellationToken cancellation)
        {
            using var listener = LoopbackListener.TryStart(LoopbackPorts)
                ?? throw new OAuthException("no_port",
                    $"Ports {LoopbackPorts.First()}–{LoopbackPorts.Last()} are all in use, so the browser has nowhere to return to. Close whatever holds them, or use an API key.");

            var clientId = await EnsureClientAsync();
            var verifier = Pkce.CreateVerifier();
            var state = Pkce.CreateState();
            openUrl(_oauth.AuthorizeUrl(clientId, listener.RedirectUri, Pkce.ChallengeFor(verifier), state));

            var callback = await listener.WaitForCallbackAsync(cancellation);
            try
            {
                await CompleteSignIn(callback, clientId, verifier, state, listener.RedirectUri);
            }
            catch (Exception e)
            {
                await callback.RespondAsync(false, e.Message.TrimEnd('.') + ". Go back to Unity and try again.");
                throw;
            }
            await callback.RespondAsync(true, "You can close this tab and go back to Unity.");
        }

        async Task CompleteSignIn(OAuthCallback callback, string clientId, string verifier, string state, string redirectUri)
        {
            if (callback.Error == "access_denied")
                throw new OAuthException(callback.Error, "Access was not granted in the browser.");
            if (callback.Error != null)
                throw new OAuthException(callback.Error, callback.ErrorDescription);
            if (callback.State != state)
                throw new OAuthException("invalid_state", "The browser answered a different sign-in.");

            TokenResponse tokens;
            try
            {
                tokens = await _oauth.ExchangeCodeAsync(clientId, callback.Code, verifier, redirectUri);
            }
            catch (OAuthException e) when (e.Error == "invalid_client")
            {
                _store.Delete(_clientKey);
                throw;
            }
            Save(tokens, clientId);
        }

        async Task<string> EnsureClientAsync()
        {
            var json = _store.Get(_clientKey);
            if (!string.IsNullOrEmpty(json))
            {
                var client = JsonUtility.FromJson<StoredOAuthClient>(json);
                if (!string.IsNullOrEmpty(client?.clientId) && client.ports != null && client.ports.SequenceEqual(LoopbackPorts))
                    return client.clientId;
            }

            var clientId = await _oauth.RegisterAsync(LoopbackPorts.Select(LoopbackListener.RedirectUriFor));
            _store.Set(_clientKey, JsonUtility.ToJson(new StoredOAuthClient { clientId = clientId, ports = LoopbackPorts }));
            return clientId;
        }

        void Save(TokenResponse tokens, string clientId) => Store(new StoredCredentials
        {
            kind = KindOAuth,
            accessToken = tokens.access_token,
            refreshToken = tokens.refresh_token,
            expiresAtUnix = (_now() + TimeSpan.FromSeconds(tokens.expires_in)).ToUnixTimeSeconds(),
            clientId = clientId,
        });

        void Store(StoredCredentials credentials) => _store.Set(_credentialsKey, JsonUtility.ToJson(credentials));

        StoredCredentials Load()
        {
            var json = _store.Get(_credentialsKey);
            if (string.IsNullOrEmpty(json)) return null;
            var stored = JsonUtility.FromJson<StoredCredentials>(json);
            return string.IsNullOrEmpty(stored?.accessToken) ? null : stored;
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
