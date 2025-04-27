using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Network;
using System;

namespace ChatdollKit.Extension.ChatMemory
{
    public class ChatMemoryIntegrator : MonoBehaviour
    {
        [SerializeField]
        private string BaseUrl;
        public string UserId;
        public string Channel;
        [SerializeField]
        private bool isDebug;
        private ChatdollHttp httpClient = new ChatdollHttp(timeout: 10000);

        public async UniTask AddHistory(string sessionId, string requestText, string responseText, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(UserId))
            {
                Debug.LogWarning("UserId is required to add history to ChatMemory service.");
                return;
            }

            if (isDebug)
            {
                Debug.Log($"AddHistory: user={UserId}, sessionId={sessionId}, request='{requestText}', response='{responseText}'");
            }

            try
            {
                await httpClient.PostJsonAsync(
                    $"{BaseUrl}/history",
                    data: new Dictionary<string, object>()
                    {
                        { "user_id", UserId },
                        { "session_id", sessionId },
                        { "messages", new List<Dictionary<string, string>>() {
                            new() { { "role", "user" }, { "content", requestText } },
                            new() { { "role", "assistant" }, { "content", responseText } }
                        }},
                        { "channel", Channel }
                    },
                    cancellationToken: token
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at adding history to ChatMemory: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public async UniTask<SearchResponse> SearchMemory(string query, int top_k = 5, bool search_content = false, bool include_retrieved_data = false, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(UserId))
            {
                Debug.LogWarning("UserId is required to search memory at ChatMemory service.");
                return new SearchResponse();
            }

            if (isDebug)
            {
                Debug.Log($"SearchMemory (Request): user={UserId}, query='{query}'");
            }

            var response = await httpClient.PostJsonAsync<SearchResponse>(
                $"{BaseUrl}/search",
                data: new Dictionary<string, object>()
                {
                    { "user_id", UserId },
                    { "query", query },
                    { "top_k", top_k },
                    { "search_content", search_content },
                    { "include_retrieved_data", include_retrieved_data }
                },
                cancellationToken: token
            );

            if (isDebug)
            {
                if (response.result != null)
                {
                    Debug.Log($"SearchMemory (Response): answer={response.result.answer}, retrieved_data='{response.result.retrieved_data}'");
                }
                else
                {
                    Debug.Log("SearchMemory (Response): No result");
                }
            }

            return response;
        }
    }

    public class SearchResponse
    {
        public SearchResult result { get; set; }
    }

    public class SearchResult
    {
        public string answer { get; set; }
        public string retrieved_data { get; set; }
    }
}
