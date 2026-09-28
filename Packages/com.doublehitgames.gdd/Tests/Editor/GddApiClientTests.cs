using System.Threading.Tasks;
using Doublehitgames.Gdd.Editor.Api;
using NUnit.Framework;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class GddApiClientTests
    {
        const string Server = "https://gdd.example.com/";

        const string SectionsJson = @"{""data"":[
            {""id"":""a"",""projectId"":""p"",""parentId"":null,""title"":""Core loop"",""content"":""# Loop"",""order"":0,""status"":""draft"",""flowchartState"":{""nodes"":[]}},
            {""id"":""b"",""projectId"":""p"",""parentId"":""a"",""title"":""Harvest"",""content"":null,""order"":1}
        ]}";

        [Test]
        public async Task ListsSectionsFromTheDataEnvelope()
        {
            var transport = new FakeTransport().Then(200, SectionsJson);
            var api = new GddApiClient(Server, transport, new FixedToken { Token = "gdd_at_x" });

            var sections = await api.ListSectionsAsync("p");

            Assert.AreEqual(2, sections.Length);
            Assert.AreEqual("Core loop", sections[0].title);
            Assert.IsTrue(string.IsNullOrEmpty(sections[0].parentId));
            Assert.AreEqual("a", sections[1].parentId);
            Assert.AreEqual(1, sections[1].order);
        }

        [Test]
        public async Task SendsTheBearerTokenToTheV1Route()
        {
            var transport = new FakeTransport().Then(200, @"{""data"":[]}");
            var api = new GddApiClient(Server, transport, new FixedToken { Token = "gdd_sk_abc" });

            await api.ListProjectsAsync();

            var request = transport.Requests[0];
            Assert.AreEqual("GET", request.Method);
            Assert.AreEqual("https://gdd.example.com/api/v1/projects", request.Url);
            Assert.AreEqual("Bearer gdd_sk_abc", request.Headers["Authorization"]);
        }

        [Test]
        public async Task RetriesOnceWithARenewedTokenAfter401()
        {
            var transport = new FakeTransport()
                .Then(401, @"{""error"":""Invalid token"",""code"":""unauthorized""}")
                .Then(200, @"{""data"":{""id"":""u"",""email"":""dev@studio.com""}}");
            var tokens = new FixedToken { Token = "old", RefreshedToken = "new" };
            var api = new GddApiClient(Server, transport, tokens);

            var me = await api.GetMeAsync();

            Assert.AreEqual("dev@studio.com", me.email);
            Assert.AreEqual(1, tokens.Refreshes);
            Assert.AreEqual("Bearer new", transport.Requests[1].Headers["Authorization"]);
        }

        [Test]
        public void ReportsUnauthorizedWhenTheTokenCannotBeRenewed()
        {
            var transport = new FakeTransport().Then(401, @"{""error"":""Invalid token"",""code"":""unauthorized""}");
            var api = new GddApiClient(Server, transport, new FixedToken { Token = "old" });

            var e = Assert.ThrowsAsync<GddApiException>(() => api.GetMeAsync());
            Assert.AreEqual(GddErrorKind.Unauthorized, e.Kind);
            Assert.AreEqual(1, transport.Requests.Count);
        }

        [Test]
        public void MapsForbiddenWithTheServerMessage()
        {
            var transport = new FakeTransport().Then(403, @"{""error"":""No access to this project"",""code"":""forbidden""}");
            var api = new GddApiClient(Server, transport, new FixedToken { Token = "t" });

            var e = Assert.ThrowsAsync<GddApiException>(() => api.ListSectionsAsync("p"));
            Assert.AreEqual(GddErrorKind.Forbidden, e.Kind);
            Assert.AreEqual("No access to this project", e.Message);
            Assert.AreEqual("forbidden", e.Code);
        }

        [Test]
        public void MapsANonJsonErrorPageToTheStatus()
        {
            var transport = new FakeTransport().Then(502, "<html>Bad gateway</html>");
            var api = new GddApiClient(Server, transport, new FixedToken { Token = "t" });

            var e = Assert.ThrowsAsync<GddApiException>(() => api.ListProjectsAsync());
            Assert.AreEqual(GddErrorKind.Server, e.Kind);
            StringAssert.Contains("502", e.Message);
        }

        [Test]
        public void ReportsNetworkFailures()
        {
            var api = new GddApiClient(Server, new FakeTransport().ThenOffline(), new FixedToken { Token = "t" });

            var e = Assert.ThrowsAsync<GddApiException>(() => api.ListProjectsAsync());
            Assert.AreEqual(GddErrorKind.Network, e.Kind);
        }

        [Test]
        public void DoesNotCallTheServerWhenSignedOut()
        {
            var transport = new FakeTransport();
            var api = new GddApiClient(Server, transport, new FixedToken { Token = null });

            var e = Assert.ThrowsAsync<GddApiException>(() => api.ListProjectsAsync());
            Assert.AreEqual(GddErrorKind.Unauthorized, e.Kind);
            Assert.IsEmpty(transport.Requests);
        }

        [Test]
        public void BuildsWebUrlsForPages()
        {
            Assert.AreEqual("https://gdd.example.com/projects/p1/sections/s1", GddServer.SectionPageUrl(Server, "p1", "s1"));
            Assert.AreEqual(GddServer.DefaultUrl, GddServer.Normalize("  "));
        }
    }
}
