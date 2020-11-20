using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace ChatdollKit.Network
{
    public class ChatdollHttp : IDisposable
    {
        private HttpClient httpClient { get; }
        public int Timeout
        {
            get
            {
                return (int)httpClient.Timeout.TotalMilliseconds;
            }
            set
            {
                httpClient.Timeout = TimeSpan.FromMilliseconds(value);
            }

        }
        public Action<string> DebugFunc { get; set; }
        public Func<HttpRequestMessage, Task> BeforeRequestFunc { get; set; }
        public Func<HttpResponseMessage, Task> AfterRequestFunc { get; set; }

        public ChatdollHttp(int timeout = 10000, Action<string> debugFunc = null, HttpClientHandler httpClientHandler = null)
        {
            httpClient = httpClientHandler == null ? new HttpClient() : new HttpClient(httpClientHandler);
            Timeout = timeout;
            DebugFunc = debugFunc;
        }

        // Get
        public async Task<HttpResponseMessage> GetAsync(string url, Dictionary<string, string> parameters = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await SendRequestAsync(url, HttpMethod.Get, null, headers, parameters);
        }

        // Get and parse JSON response
        public async Task<TResponse> GetJsonAsync<TResponse>(string url, Dictionary<string, string> parameters = null, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await SendRequestAsync(url, HttpMethod.Get, null, headers, parameters, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync();
            DebugFunc?.Invoke($"Response JSON: {responseString}");
            return JsonConvert.DeserializeObject<TResponse>(responseString);
        }

        // Post form data as Key-Values
        public async Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var formContent = new FormUrlEncodedContent(data);
            return await SendRequestAsync(url, HttpMethod.Post, formContent, headers, parameters, cancellationToken);
        }

        // Post form data and parse JSON response
        public async Task<TResponse> PostFormAsync<TResponse>(string url, Dictionary<string, string> data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await PostFormAsync(url, data, headers, parameters, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync();
            DebugFunc?.Invoke($"Response JSON: {responseString}");
            return JsonConvert.DeserializeObject<TResponse>(responseString);
        }

        // Post binary data
        public async Task<HttpResponseMessage> PostBytesAsync(string url, byte[] data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var bytesContent = new ByteArrayContent(data);
            return await SendRequestAsync(url, HttpMethod.Post, bytesContent, headers, parameters, cancellationToken);
        }

        // Post binary data and parse JSON response
        public async Task<TResponse> PostBytesAsync<TResponse>(string url, byte[] data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await PostBytesAsync(url, data, headers, parameters, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync();
            DebugFunc?.Invoke($"Response JSON: {responseString}");
            return JsonConvert.DeserializeObject<TResponse>(responseString);
        }

        // Post JSON data
        public async Task<HttpResponseMessage> PostJsonAsync(string url, object data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var json = JsonConvert.SerializeObject(data);
            var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");
            return await SendRequestAsync(url, HttpMethod.Post, jsonContent, headers, parameters, cancellationToken);
        }

        // Post JSON data and parse JSON response
        public async Task<TResponse> PostJsonAsync<TResponse>(string url, object data, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await PostJsonAsync(url, data, headers, parameters, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync();
            DebugFunc?.Invoke($"Response JSON: {responseString}");
            return JsonConvert.DeserializeObject<TResponse>(responseString);
        }

        // Send http request
        public async Task<HttpResponseMessage> SendRequestAsync(string url, HttpMethod httpMethod, HttpContent content, Dictionary<string, string> headers = null, Dictionary<string, string> parameters = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Create request
            var request = new HttpRequestMessage(
                httpMethod ?? HttpMethod.Get,
                parameters == null ? url : $"{url}?{await new FormUrlEncodedContent(parameters).ReadAsStringAsync()}");

            DebugFunc?.Invoke($"Method: {request.Method}");
            DebugFunc?.Invoke($"URI: {request.RequestUri}");

            // Set headers
            if (headers != null)
            {
                foreach (var h in headers)
                {
                    request.Headers.Add(h.Key, h.Value);
                }
            }
            DebugFunc?.Invoke($"Request headers: {request.Headers}");

            // Set content
            if (content != null)
            {
                request.Content = content;
                if (content is FormUrlEncodedContent || content is StringContent)
                {
                    DebugFunc?.Invoke($"Content: {await request.Content?.ReadAsStringAsync()}");
                }
                else
                {
                    DebugFunc?.Invoke($"Content: byte[{(await request.Content.ReadAsByteArrayAsync()).Length}]");
                }
            }

            // Inject user function just before sending request
            if (BeforeRequestFunc != null)
            {
                await BeforeRequestFunc(request);
            }

            // Send request
            var response = await httpClient.SendAsync(request, cancellationToken);

            // Inject user function just after receiving response
            if (AfterRequestFunc != null)
            {
                await AfterRequestFunc(response);
            }

            DebugFunc?.Invoke($"Status code: {(int)response.StatusCode}");
            DebugFunc?.Invoke($"Response headers: {response.Headers}");

            // Throw exception if the status code is not success
            if (!response.IsSuccessStatusCode)
            {
                if (response.Content != null)
                {
                    DebugFunc?.Invoke($"Response content: {await response.Content.ReadAsStringAsync()}");
                }
                response.EnsureSuccessStatusCode();
            }

            return response;
        }

        // Get `Authorization` header value for Basic Auth
        public AuthenticationHeaderValue GetBasicAuthenticationHeaderValue(string user, string password)
        {
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{password}")));
        }

        // Dispose
        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}
