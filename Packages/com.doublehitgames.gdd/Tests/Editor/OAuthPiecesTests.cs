using System;
using System.Text.RegularExpressions;
using Doublehitgames.Gdd.Editor.Auth;
using NUnit.Framework;

namespace Doublehitgames.Gdd.Editor.Tests
{
    public class OAuthPiecesTests
    {
        [Test]
        public void ChallengeIsBase64UrlOfSha256()
        {
            // Cross-checked outside Unity:
            //   printf %s <verifier> | openssl dgst -sha256 -binary | openssl base64 | tr '+/' '-_' | tr -d '='
            Assert.AreEqual("N4-5v7LG-T8x3fWSLZ-7W4IevaqpPl0Ln-6oRgYUj1Q",
                Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFbEjXk"));
        }

        [Test]
        public void VerifiersAreUrlSafeAndFresh()
        {
            var a = Pkce.CreateVerifier();
            var b = Pkce.CreateVerifier();
            Assert.AreEqual(43, a.Length);
            Assert.IsTrue(Regex.IsMatch(a, "^[A-Za-z0-9_-]+$"), a);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void ReadsTheCallbackRequest()
        {
            var cb = LoopbackListener.ParseRequestLine("GET /callback?code=gdd_ac_1%2B2&state=s-1 HTTP/1.1");
            Assert.AreEqual("gdd_ac_1+2", cb.Code);
            Assert.AreEqual("s-1", cb.State);
            Assert.IsNull(cb.Error);
        }

        [Test]
        public void ReadsADeniedConsent()
        {
            var cb = LoopbackListener.ParseRequestLine("GET /callback?error=access_denied&error_description=User+said+no&state=s HTTP/1.1");
            Assert.AreEqual("access_denied", cb.Error);
            Assert.AreEqual("User said no", cb.ErrorDescription);
            Assert.IsNull(cb.Code);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("GET /favicon.ico HTTP/1.1")]
        [TestCase("POST /callback?code=x HTTP/1.1")]
        [TestCase("GET /callbackx?code=x HTTP/1.1")]
        public void IgnoresAnythingElse(string line)
        {
            Assert.IsNull(LoopbackListener.ParseRequestLine(line));
        }

        [Test]
        public void AuthorizeUrlCarriesPkceAndState()
        {
            var url = new OAuthClient("https://gdd.example.com/", new FakeTransport())
                .AuthorizeUrl("client-1", "http://127.0.0.1:47811/callback", "challenge", "state-1");

            var uri = new Uri(url);
            Assert.AreEqual("/oauth/authorize", uri.AbsolutePath);
            var q = LoopbackListener.ParseQuery(uri.Query.TrimStart('?'));
            Assert.AreEqual("code", q["response_type"]);
            Assert.AreEqual("client-1", q["client_id"]);
            Assert.AreEqual("http://127.0.0.1:47811/callback", q["redirect_uri"]);
            Assert.AreEqual("challenge", q["code_challenge"]);
            Assert.AreEqual("S256", q["code_challenge_method"]);
            Assert.AreEqual("state-1", q["state"]);
        }
    }
}
