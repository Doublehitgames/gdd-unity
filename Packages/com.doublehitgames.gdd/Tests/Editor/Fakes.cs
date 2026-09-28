using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Api;
using Doublehitgames.Gdd.Editor.Auth;

namespace Doublehitgames.Gdd.Editor.Tests
{
    /// <summary>Answers requests from a script, in order, and remembers what was asked.</summary>
    internal sealed class FakeTransport : IHttpTransport
    {
        readonly Queue<Func<HttpRequest, HttpResponse>> _answers = new Queue<Func<HttpRequest, HttpResponse>>();
        public readonly List<HttpRequest> Requests = new List<HttpRequest>();

        public FakeTransport Then(long status, string body) =>
            Then(_ => new HttpResponse { Status = status, Body = body });

        public FakeTransport ThenOffline() =>
            Then(_ => new HttpResponse { Status = 0, NetworkError = "Cannot resolve destination host" });

        public FakeTransport Then(Func<HttpRequest, HttpResponse> answer)
        {
            _answers.Enqueue(answer);
            return this;
        }

        public Task<HttpResponse> SendAsync(HttpRequest request)
        {
            Requests.Add(request);
            if (_answers.Count == 0) throw new InvalidOperationException("Unexpected request: " + request.Method + " " + request.Url);
            return Task.FromResult(_answers.Dequeue()(request));
        }
    }

    internal sealed class MemoryCredentialStore : ICredentialStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();

        public string Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => Values[key] = value;
        public void Delete(string key) => Values.Remove(key);
    }

    internal sealed class FixedToken : ITokenSource
    {
        public string Token;
        public string RefreshedToken;
        public int Refreshes;

        public Task<string> GetTokenAsync() => Task.FromResult(Token);

        public Task<bool> TryRefreshAsync()
        {
            Refreshes++;
            if (RefreshedToken == null) return Task.FromResult(false);
            Token = RefreshedToken;
            return Task.FromResult(true);
        }
    }
}
