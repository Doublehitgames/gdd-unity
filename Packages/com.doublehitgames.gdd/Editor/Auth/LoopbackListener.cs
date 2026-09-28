using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Doublehitgames.Gdd.Editor.Auth
{
    internal sealed class OAuthCallback
    {
        public string Code;
        public string State;
        public string Error;
        public string ErrorDescription;

        internal Func<bool, string, Task> Responder;

        /// <summary>Tells the waiting browser tab how the sign-in ended.</summary>
        public Task RespondAsync(bool ok, string message) => Responder?.Invoke(ok, message) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Catches the browser coming back from the consent page (RFC 8252 loopback
    /// redirect). A bare TcpListener rather than HttpListener: nothing to
    /// register with the OS, and it behaves the same on Windows, macOS and Linux.
    /// </summary>
    internal sealed class LoopbackListener : IDisposable
    {
        public const string CallbackPath = "/callback";
        static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

        readonly TcpListener _listener;

        public int Port { get; }
        public string RedirectUri => RedirectUriFor(Port);

        LoopbackListener(TcpListener listener, int port)
        {
            _listener = listener;
            Port = port;
        }

        public static string RedirectUriFor(int port) => $"http://127.0.0.1:{port}{CallbackPath}";

        /// <summary>
        /// Binds the first free port of the list. The ports are fixed because GDD
        /// Manager matches the redirect URI exactly, port included.
        /// </summary>
        public static LoopbackListener TryStart(IEnumerable<int> ports)
        {
            foreach (var port in ports)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                    return new LoopbackListener(listener, port);
                }
                catch (SocketException)
                {
                    // Taken by something else; try the next one.
                }
            }
            return null;
        }

        public async Task<OAuthCallback> WaitForCallbackAsync(CancellationToken cancellation)
        {
            // Asynchronous continuations: the sign-in must not resume inside the
            // connection handler that is still answering the browser.
            var result = new TaskCompletionSource<OAuthCallback>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellation.Register(() => result.TrySetCanceled()))
            {
                _ = AcceptLoop(result);
                try
                {
                    return await result.Task;
                }
                finally
                {
                    _listener.Stop();
                }
            }
        }

        async Task AcceptLoop(TaskCompletionSource<OAuthCallback> result)
        {
            while (!result.Task.IsCompleted)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception e)
                {
                    // Stop() during a cancel lands here too; TrySet keeps the first outcome.
                    result.TrySetException(e);
                    return;
                }
                // Browsers open speculative connections that never send a request,
                // so each one is served on its own instead of blocking the loop.
                _ = Serve(client, result);
            }
        }

        static async Task Serve(TcpClient client, TaskCompletionSource<OAuthCallback> result)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var read = new StreamReader(stream, Encoding.ASCII).ReadLineAsync();
                    if (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10))) != read) return;

                    var callback = ParseRequestLine(await read);
                    if (callback == null)
                    {
                        // A favicon or a stray request: not the redirect.
                        await WriteResponse(stream, "404 Not Found", "");
                        return;
                    }

                    // The browser tab stays open until the sign-in says how it went,
                    // so it never announces a success that has not happened yet.
                    var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    callback.Responder = async (ok, message) =>
                    {
                        try
                        {
                            await WriteResponse(stream, "200 OK", ResultPage(ok, message));
                        }
                        catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
                        {
                            // The tab was closed first; Unity still has the outcome.
                        }
                        finally
                        {
                            answered.TrySetResult(true);
                        }
                    };

                    if (!result.TrySetResult(callback))
                    {
                        await callback.RespondAsync(false, "This sign-in was already handled. Go back to Unity.");
                        return;
                    }
                    await Task.WhenAny(answered.Task, Task.Delay(ResponseTimeout));
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
                {
                    // The browser hung up; the real redirect comes on another connection.
                }
            }
        }

        /// <summary>Reads <c>GET /callback?code=…&amp;state=… HTTP/1.1</c>; null for any other request.</summary>
        internal static OAuthCallback ParseRequestLine(string requestLine)
        {
            if (string.IsNullOrEmpty(requestLine)) return null;
            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET") return null;

            var target = parts[1];
            var q = target.IndexOf('?');
            var path = q < 0 ? target : target.Substring(0, q);
            if (path != CallbackPath) return null;

            var query = ParseQuery(q < 0 ? "" : target.Substring(q + 1));
            query.TryGetValue("code", out var code);
            query.TryGetValue("state", out var state);
            query.TryGetValue("error", out var error);
            query.TryGetValue("error_description", out var description);
            return new OAuthCallback { Code = code, State = state, Error = error, ErrorDescription = description };
        }

        internal static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair.Substring(0, eq);
                var value = eq < 0 ? "" : pair.Substring(eq + 1);
                result[Decode(key)] = Decode(value);
            }
            return result;
        }

        static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        static async Task WriteResponse(Stream stream, string status, string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, 0, head.Length);
            await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        static string ResultPage(bool ok, string message)
        {
            var heading = ok ? "Signed in to GDD Manager" : "Sign-in did not complete";
            return "<!doctype html><meta charset=utf-8><title>GDD Manager</title>"
                 + "<body style=\"font-family:system-ui,sans-serif;display:grid;place-items:center;height:100vh;margin:0\">"
                 + $"<div style=\"max-width:32rem;text-align:center\"><h2>{heading}</h2><p>{WebUtility.HtmlEncode(message)}</p></div></body>";
        }

        public void Dispose() => _listener.Stop();
    }
}
