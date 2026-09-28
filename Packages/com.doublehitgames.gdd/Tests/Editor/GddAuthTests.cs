using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Auth;
using NUnit.Framework;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class GddAuthTests
    {
        const string Server = "https://gdd.example.com";
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        MemoryCredentialStore _store;
        FakeTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _store = new MemoryCredentialStore();
            _transport = new FakeTransport();
        }

        GddAuth Auth(string env = null, Func<DateTimeOffset> now = null) =>
            new GddAuth(Server, _store, _transport, now ?? (() => Now), name => name == GddAuth.EnvironmentVariable ? env : null);

        static string Tokens(string access, string refresh, long expiresIn = 604800) =>
            $@"{{""access_token"":""{access}"",""token_type"":""bearer"",""expires_in"":{expiresIn},""refresh_token"":""{refresh}"",""scope"":""gdd""}}";

        /// <summary>Signs in through the whole browser round trip, with the browser played by the test.</summary>
        async Task<Task<string>> SignIn(GddAuth auth, string access = "gdd_at_1", string refresh = "gdd_rt_1", long expiresIn = 604800)
        {
            _transport
                .Then(201, @"{""client_id"":""client-1""}")
                .Then(200, Tokens(access, refresh, expiresIn));
            Task<string> page = null;
            await auth.SignInWithBrowserAsync(url => page = Browser(url, "the-code"), CancellationToken.None);
            return page;
        }

        /// <summary>
        /// What the consent page does on "Allow": redirect to the loopback with a
        /// code and the same state. Returns the page the tab ends up showing.
        /// </summary>
        static async Task<string> Browser(string authorizeUrl, string code, string stateOverride = null)
        {
            var query = LoopbackListener.ParseQuery(new Uri(authorizeUrl).Query.TrimStart('?'));
            var redirect = new Uri(query["redirect_uri"]);
            var state = stateOverride ?? query["state"];

            // A speculative connection that never sends anything must not block the real one.
            using var idle = new TcpClient();
            await idle.ConnectAsync("127.0.0.1", redirect.Port);

            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", redirect.Port);
            var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                $"GET {redirect.AbsolutePath}?code={code}&state={Uri.EscapeDataString(state)} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
            await stream.WriteAsync(request, 0, request.Length);
            return await new StreamReader(stream).ReadToEndAsync();
        }

        [Test]
        public async Task BrowserSignInRegistersOnceAndExchangesTheCodeWithPkce()
        {
            var auth = Auth();
            var page = await SignIn(auth);

            StringAssert.Contains("Signed in to GDD Manager", await page);
            Assert.AreEqual(CredentialSource.OAuth, auth.Source);
            Assert.AreEqual("gdd_at_1", await auth.GetTokenAsync());

            var register = _transport.Requests[0];
            Assert.AreEqual(Server + "/api/oauth/register", register.Url);
            foreach (var port in GddAuth.LoopbackPorts)
                StringAssert.Contains($"http://127.0.0.1:{port}/callback", register.Body);

            var exchange = LoopbackListener.ParseQuery(_transport.Requests[1].Body);
            Assert.AreEqual("authorization_code", exchange["grant_type"]);
            Assert.AreEqual("client-1", exchange["client_id"]);
            Assert.AreEqual("the-code", exchange["code"]);
            Assert.AreEqual(43, exchange["code_verifier"].Length);

            // A second sign-in reuses the registered client.
            _transport.Then(200, Tokens("gdd_at_2", "gdd_rt_2"));
            await auth.SignInWithBrowserAsync(url => _ = Browser(url, "code-2"), CancellationToken.None);
            Assert.AreEqual(3, _transport.Requests.Count);
            Assert.AreEqual("gdd_at_2", await auth.GetTokenAsync());
        }

        // Assert.ThrowsAsync blocks the main thread, which the socket continuations
        // need: awaiting is the only way to wait for a real loopback round trip.
        static async Task<T> Throws<T>(Func<Task> action) where T : Exception
        {
            try
            {
                await action();
            }
            catch (T e)
            {
                return e;
            }
            Assert.Fail($"Expected {typeof(T).Name}.");
            return null;
        }

        [Test]
        public async Task BrowserSignInRejectsAForeignState()
        {
            var auth = Auth();
            _transport.Then(201, @"{""client_id"":""client-1""}");

            Task<string> page = null;
            var e = await Throws<OAuthException>(() =>
                auth.SignInWithBrowserAsync(url => page = Browser(url, "code", stateOverride: "forged"), CancellationToken.None));
            Assert.AreEqual("invalid_state", e.Error);
            Assert.AreEqual(CredentialSource.None, auth.Source);
            StringAssert.Contains("Sign-in did not complete", await page);
        }

        [Test]
        public async Task TheBrowserTabShowsAFailedExchangeInsteadOfSuccess()
        {
            var auth = Auth();
            _transport
                .Then(201, @"{""client_id"":""client-1""}")
                .Then(400, @"{""error"":""invalid_grant"",""error_description"":""Authorization code expirado""}");

            Task<string> page = null;
            var e = await Throws<OAuthException>(() =>
                auth.SignInWithBrowserAsync(url => page = Browser(url, "late-code"), CancellationToken.None));

            Assert.AreEqual("invalid_grant", e.Error);
            var html = await page;
            StringAssert.Contains("Sign-in did not complete", html);
            StringAssert.Contains("Authorization code expirado", html);
            StringAssert.DoesNotContain("Signed in to", html);
        }

        [Test]
        public async Task BrowserSignInCanBeCancelled()
        {
            var auth = Auth();
            _transport.Then(201, @"{""client_id"":""client-1""}");
            using var cancel = new CancellationTokenSource();

            await Throws<OperationCanceledException>(() => auth.SignInWithBrowserAsync(_ => cancel.Cancel(), cancel.Token));
            // The port is free again for the next attempt.
            using var listener = LoopbackListener.TryStart(GddAuth.LoopbackPorts.Take(1));
            Assert.IsNotNull(listener);
        }

        [Test]
        public async Task RefreshesAheadOfExpiry()
        {
            var clock = Now;
            var auth = Auth(now: () => clock);
            await SignIn(auth, expiresIn: 3600);

            clock = Now.AddMinutes(58);
            _transport.Then(200, Tokens("gdd_at_new", "gdd_rt_new"));

            Assert.AreEqual("gdd_at_new", await auth.GetTokenAsync());
            var refresh = LoopbackListener.ParseQuery(_transport.Requests.Last().Body);
            Assert.AreEqual("refresh_token", refresh["grant_type"]);
            Assert.AreEqual("gdd_rt_1", refresh["refresh_token"]);
        }

        [Test]
        public async Task ARefusedRefreshSignsOut()
        {
            var auth = Auth();
            await SignIn(auth);
            _transport.Then(400, @"{""error"":""invalid_grant"",""error_description"":""Refresh token expired""}");

            Assert.IsFalse(await auth.TryRefreshAsync());
            Assert.AreEqual(CredentialSource.None, auth.Source);
        }

        [Test]
        public async Task ARefreshLostToAnotherEditorKeepsTheNewerTokens()
        {
            var auth = Auth();
            await SignIn(auth);
            var otherEditor = Auth();
            _transport.Then(request =>
            {
                // While this refresh is on the wire, the other editor on the machine rotates first.
                _transport.Then(200, Tokens("gdd_at_other", "gdd_rt_other"));
                Assert.IsTrue(otherEditor.TryRefreshAsync().Result);
                return new Api.HttpResponse { Status = 400, Body = @"{""error"":""invalid_grant""}" };
            });

            Assert.IsTrue(await auth.TryRefreshAsync());
            Assert.AreEqual("gdd_at_other", await auth.GetTokenAsync());
        }

        [Test]
        public async Task APastedKeyIsUsedAsIs()
        {
            var auth = Auth();
            auth.UseApiKey("  gdd_sk_0123  ");

            Assert.AreEqual(CredentialSource.ApiKey, auth.Source);
            Assert.AreEqual("gdd_sk_0123", await auth.GetTokenAsync());
            Assert.IsFalse(await auth.TryRefreshAsync());
        }

        [Test]
        public void RejectsSomethingThatIsNotAKey()
        {
            Assert.Throws<ArgumentException>(() => Auth().UseApiKey("gdd_at_123"));
        }

        [Test]
        public async Task FallsBackToTheEnvironmentKey()
        {
            var auth = Auth(env: "gdd_sk_ci");

            Assert.AreEqual(CredentialSource.Environment, auth.Source);
            Assert.AreEqual("gdd_sk_ci", await auth.GetTokenAsync());

            auth.UseApiKey("gdd_sk_mine");
            Assert.AreEqual("gdd_sk_mine", await auth.GetTokenAsync());
        }

        [Test]
        public async Task CredentialsAreKeptPerServer()
        {
            Auth().UseApiKey("gdd_sk_prod");
            var selfHosted = new GddAuth("https://gdd.studio.internal", _store, _transport, () => Now, _ => null);

            Assert.AreEqual(CredentialSource.None, selfHosted.Source);
            Assert.IsNull(await selfHosted.GetTokenAsync());
        }
    }
}
