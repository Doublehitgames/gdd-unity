using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Doublehitgames.Gdd.Editor.Api
{
    internal sealed class HttpRequest
    {
        public string Method = "GET";
        public string Url;
        public string Body;
        public string ContentType;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
    }

    internal sealed class HttpResponse
    {
        /// <summary>HTTP status, or 0 when the request never got an answer.</summary>
        public long Status;
        public string Body;
        /// <summary>Why there is no answer (DNS, TLS, offline…). Null when Status is set.</summary>
        public string NetworkError;

        public bool IsSuccess => Status >= 200 && Status < 300;
    }

    /// <summary>
    /// The one seam between the plugin and the network, so everything above it
    /// can be tested with canned responses.
    /// </summary>
    internal interface IHttpTransport
    {
        Task<HttpResponse> SendAsync(HttpRequest request);
    }

    /// <summary>
    /// UnityWebRequest honours the editor's proxy and certificate settings, which
    /// a raw HttpClient would not. Must be called from the main thread.
    /// </summary>
    internal sealed class UnityWebRequestTransport : IHttpTransport
    {
        const int TimeoutSeconds = 30;

        public Task<HttpResponse> SendAsync(HttpRequest request)
        {
            var tcs = new TaskCompletionSource<HttpResponse>();
            var web = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            if (request.Body != null)
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body))
                {
                    contentType = request.ContentType ?? "application/json",
                };
            }
            foreach (var header in request.Headers)
                web.SetRequestHeader(header.Key, header.Value);

            web.SendWebRequest().completed += _ =>
            {
                var response = new HttpResponse();
                if (web.result == UnityWebRequest.Result.ConnectionError)
                    response.NetworkError = web.error;
                else
                {
                    response.Status = web.responseCode;
                    response.Body = web.downloadHandler.text;
                }
                web.Dispose();
                tcs.SetResult(response);
            };
            return tcs.Task;
        }
    }
}
