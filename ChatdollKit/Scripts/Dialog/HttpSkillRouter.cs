using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Network;

namespace ChatdollKit.Dialog
{
    public class HttpSkillRouter : SkillRouterBase
    {
        public string IntentExtractorUri;
        public string SkillsUri;
        public bool LoadSkillsOnStart = true;
        protected ChatdollHttp httpClient = new ChatdollHttp();

        public async UniTask Start()
        {
            if (LoadSkillsOnStart)
            {
                try
                {
                    await RegisterSkillsAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occured in loading skills: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        public override async UniTask<IntentExtractionResult> ExtractIntentAsync(Request request, State state, CancellationToken token)
        {
            var httpIntentResponse = await httpClient.PostJsonAsync<HttpIntentResponse>(
                IntentExtractorUri, new HttpIntentRequest(request, state));

            if (httpIntentResponse.Error != null)
            {
                throw httpIntentResponse.Error.Exception;
            }

            return httpIntentResponse.IntentExtractionResult;
        }

        public override ISkill Route(Request request, State state, CancellationToken token)
        {
            // Register skill dynamically
            RegisterHttpSkill(request.Intent.Name);

            return base.Route(request, state, token);
        }

        public async UniTask RegisterSkillsAsync()
        {
            var httpSkillsResponse = await httpClient.GetJsonAsync<HttpSkillsResponse>(SkillsUri);

            if (httpSkillsResponse.Error != null)
            {
                throw httpSkillsResponse.Error.Exception;
            }

            foreach (var skillName in httpSkillsResponse.SkillNames)
            {
                RegisterHttpSkill(skillName);
            }
        }

        public void RegisterHttpSkill(string SkillName)
        {
            if (!topicResolver.ContainsKey(SkillName))
            {
                var skill = gameObject.AddComponent<HttpSkillBase>();
                skill.Name = SkillName;
                skill.Uri = SkillsUri.EndsWith("/") ?
                    SkillsUri + SkillName : SkillsUri + "/" + SkillName;
                RegisterSkill(skill);
            }
        }

        // Request message to /intent
        private class HttpIntentRequest
        {
            public Request Request { get; set; }
            public State State { get; set; }

            public HttpIntentRequest(Request request, State state)
            {
                Request = request;
                State = state;
            }
        }

        // Response message from /intent
        private class HttpIntentResponse
        {
            public IntentExtractionResult IntentExtractionResult { get; set; }
            public HttpIntentError Error { get; set; }
        }

        // Response message from /skills
        private class HttpSkillsResponse
        {
            public List<string> SkillNames { get; set; }
            public HttpIntentError Error { get; set; }
        }

        // Error info in response
        private class HttpIntentError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public Exception Exception { get; set; }
        }
    }
}
