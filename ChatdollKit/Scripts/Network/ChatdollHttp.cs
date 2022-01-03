using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace ChatdollKit.Network
{
    public class ChatdollHttp
    {
        public int Timeout;
        public Action<string> DebugFunc { get; set; }
        public Func<UnityWebRequest, UniTask> BeforeRequestFunc { get; set; }
        public Func<UnityWebRequest, UniTask> AfterRequestFunc { get; set; }

        public ChatdollHttp(int timeout = 10000, Action<string> debugFunc = null)
        {
            Timeout = timeout;
            DebugFunc = debugFunc;
        }

        // Get
        public async UniTask<ChatdollHttpResponse> GetAsync(string url, Dictionary<string, string> parameters = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await SendRequestAsync(url, "GET", null, headers, parameters);
        }

        // Get and parse JSON response
        public async UniTask<TResponse> GetJsonAsync<TResponse>(string url, Dictionary<string, string> parameters = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await SendRequestAsync(url, "GET", null, headers, parameters, cancellationToken);
            DebugFunc?.Invoke($"Response JSON: {response.Data}");
            return JsonConvert.DeserializeObject<TResponse>((string)response.Data);
        }

        // Post form data as Key-Values
        public async UniTask<ChatdollHttpResponse> PostFormAsync(string url, Dictionary<string, string> data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await SendRequestAsync(url, "POST", data, headers, parameters, cancellationToken);
        }

        // Post form data and parse JSON response
        public async UniTask<TResponse> PostFormAsync<TResponse>(string url, Dictionary<string, string> data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await PostFormAsync(url, data, headers, parameters, cancellationToken);
            DebugFunc?.Invoke($"Response JSON: {(string)response.Data}");
            return JsonConvert.DeserializeObject<TResponse>((string)response.Data);
        }

        // Post binary data
        public async UniTask<ChatdollHttpResponse> PostBytesAsync(string url, byte[] data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await SendRequestAsync(url, "POST", data, headers, parameters, cancellationToken);
        }

        // Post binary data and parse JSON response
        public async UniTask<TResponse> PostBytesAsync<TResponse>(string url, byte[] data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            DebugFunc?.Invoke($"data size: {data.Length}");

            var response = await PostBytesAsync(url, data, headers, parameters, cancellationToken);
            DebugFunc?.Invoke($"Response JSON: {(string)response.Data}");
            return JsonConvert.DeserializeObject<TResponse>((string)response.Data);
        }

        // Post JSON data
        public async UniTask<ChatdollHttpResponse> PostJsonAsync(string url, object data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var json = JsonConvert.SerializeObject(data);
            return await SendRequestAsync(url, "POST", json, headers, parameters, cancellationToken);
        }

        // Post JSON data and parse JSON response
        public async UniTask<TResponse> PostJsonAsync<TResponse>(string url, object data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (headers == null)
            {
                headers = new Dictionary<string, string>();
            }
            if (!headers.ContainsKey("Content-Type"))
            {
                headers.Add("Content-Type", "application/json");
            }
            var response = await PostJsonAsync(url, data, headers, parameters, cancellationToken);
            DebugFunc?.Invoke($"Response JSON: {(string)response.Data}");
            return JsonConvert.DeserializeObject<TResponse>((string)response.Data);
        }

        // Send http request
        public async UniTask<ChatdollHttpResponse> SendRequestAsync(string url, string method, object content, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) { return null; };

            if (parameters != null)
            {
                var escapedParams = new List<string>();
                foreach (var p in parameters)
                {
                    escapedParams.Add(p.Key + "=" + UnityWebRequest.EscapeURL(p.Value, Encoding.UTF8));
                }
                url += "?" + string.Join("&", escapedParams);
            }

            using (UnityWebRequest request = new UnityWebRequest(url, method ?? "GET"))
            {
                DebugFunc?.Invoke($"Method: {request.method}");
                DebugFunc?.Invoke($"URI: {request.uri}");

                // Form
                if (content is Dictionary<string, string>)
                {
                    var data = new List<IMultipartFormSection>();
                    foreach (var d in (Dictionary<string, string>)content)
                    {
                        data.Add(new MultipartFormDataSection(d.Key, d.Value));
                    }

                    var boundary = UnityWebRequest.GenerateBoundary();
                    var formSections = UnityWebRequest.SerializeFormSections(data, boundary);
                    request.uploadHandler = new UploadHandlerRaw(formSections);
                }
                // JSON
                else if (content is string)
                {
                    var data = Encoding.UTF8.GetBytes((string)content);
                    request.uploadHandler = new UploadHandlerRaw(data);
                }
                // Binary
                else if (content is byte[])
                {
                    request.uploadHandler = new UploadHandlerRaw((byte[])content);
                }

                // Set headers
                if (headers != null)
                {
                    foreach (var h in headers)
                    {
                        request.SetRequestHeader(h.Key, h.Value);
                    }
                }

                // Inject user function just before sending request
                if (BeforeRequestFunc != null)
                {
                    await BeforeRequestFunc(request);
                }

                request.downloadHandler = new DownloadHandlerBuffer();

                await request.SendWebRequest();

                DebugFunc?.Invoke($"Status code: {request.responseCode}");

                // Inject user function just after receiving response
                if (AfterRequestFunc != null)
                {
                    await AfterRequestFunc(request);
                }

                // Throw exception if the status code is not success
                if (request.isNetworkError || request.isHttpError)
                {
                    DebugFunc?.Invoke($"Error: {request.error} \n {request.downloadHandler.text}");

                    throw new Exception($"Error: {request.error} \n {request.downloadHandler.text}");
                }

                // TODO: Enable to switch to get response data as bytes/text
                //var responseData = request.downloadHandler.data;
                return new ChatdollHttpResponse((int)request.responseCode, request.GetResponseHeaders(), request.downloadHandler.text);
            }
        }

        // Get `Authorization` header value for Basic Auth
        public string GetBasicAuthenticationHeaderValue(string user, string password)
        {
            return "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{password}"));
        }
    }

    public class ChatdollHttpResponse
    {
        public int StatusCode;
        public Dictionary<string, string> Headers;
        public object Data;

        public ChatdollHttpResponse(int statusCode, Dictionary<string, string> headers, object data)
        {
            StatusCode = statusCode;
            Headers = headers;
            Data = data;
        }
    }
}
