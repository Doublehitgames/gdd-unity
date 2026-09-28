using System;
using System.Security.Cryptography;
using System.Text;

namespace Doublehitgames.Gdd.Editor.Auth
{
    /// <summary>PKCE (RFC 7636), S256 only — the one method GDD Manager accepts.</summary>
    internal static class Pkce
    {
        /// <summary>43 characters from 32 random bytes, the shortest verifier the RFC allows.</summary>
        public static string CreateVerifier() => RandomToken(32);

        public static string ChallengeFor(string verifier)
        {
            using var sha = SHA256.Create();
            return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        /// <summary>Unguessable value for the OAuth <c>state</c> parameter.</summary>
        public static string CreateState() => RandomToken(16);

        static string RandomToken(int bytes)
        {
            var buffer = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buffer);
            return Base64Url(buffer);
        }

        internal static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
