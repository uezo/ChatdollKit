using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog.Processor
{
    public class RemoteRequestProcessor : MonoBehaviour, IRequestProcessorWithPrompt
    {
        public string BaseUrl = string.Empty;
        protected ChatdollHttp client = new ChatdollHttp();
        protected ModelController modelController;
        public Dictionary<string, Func<Response, CancellationToken, UniTask>> ResponseHandlers = new Dictionary<string, Func<Response, CancellationToken, UniTask>>();

        protected virtual void Awake()
        {
            modelController = GetComponent<ModelController>();
        }

        protected virtual void Start()
        {
            if (string.IsNullOrEmpty(BaseUrl))
            {
                Debug.LogWarning("Base Url of the remote server is not configured.");
            }
            else
            {
                // Warm up server
                _ = client.GetAsync(BaseUrl + "/ping");
            }
        }

        public virtual async UniTask PromptAsync(DialogRequest dialogRequest, CancellationToken token)
        {
            AnimatedVoiceRequest promptAnimatedVoice = null;

            // Update prompt animated voice and request type
            var httpPromptResponse = await client.PostJsonAsync<Response>(BaseUrl + "/prompt", dialogRequest);
            if (httpPromptResponse != null)
            {
                promptAnimatedVoice = httpPromptResponse.AnimatedVoiceRequest;
            }

            // Show animated voice
            if (promptAnimatedVoice != null)
            {
                await modelController.AnimatedSay(promptAnimatedVoice, token);
            }
        }

        public virtual async UniTask<Response> ProcessRequestAsync(Request request, CancellationToken token)
        {
            try
            {
                if (!request.IsSet() || request.IsCanceled)
                {
                    if (!string.IsNullOrEmpty(request.ClientId))
                    {
                        // Clear state when request is not set or canceled
                        await client.PostJsonAsync(BaseUrl + "/state/reset", request);
                    }

                    return null;
                }

                if (token.IsCancellationRequested) { return null; }

                var httpSkillResponse = await client.PostJsonAsync<Response>(BaseUrl + "/process", request);

                if (!string.IsNullOrEmpty(httpSkillResponse.SkillName) && ResponseHandlers.ContainsKey(httpSkillResponse.SkillName))
                {
                    await ResponseHandlers[httpSkillResponse.SkillName].Invoke(httpSkillResponse, token);
                }
                else
                {
                    await DefaultResponseHandler(httpSkillResponse, token);
                }

                return httpSkillResponse;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error at ProcessRequestAsync: {ex.Message}\n{ex.StackTrace}");

                if (!string.IsNullOrEmpty(request.ClientId))
                {
                    await client.PostJsonAsync<Response>(BaseUrl + "/state/reset", request);
                }

                throw ex;
            }
        }

        public virtual async UniTask DefaultResponseHandler(Response response, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (response.AnimatedVoiceRequest != null)
            {
                await modelController.AnimatedSay(response.AnimatedVoiceRequest, token);
            }
        }
    }
}
