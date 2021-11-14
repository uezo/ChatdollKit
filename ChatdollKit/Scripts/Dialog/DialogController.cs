using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ChatdollKit.Model;

namespace ChatdollKit.Dialog
{
    public class DialogController
    {
        // Conversation
        public bool IsChatting { get; private set; }
        public bool IsError { get; private set; }
        public IUserStore UserStore { get; private set; }
        public IStateStore StateStore { get; private set; }
        public ISkillRouter SkillRouter { get; private set; }
        public Dictionary<RequestType, IRequestProvider> RequestProviders { get; private set; }

        // Model
        public ModelController ModelController { get; set; }

        // TokenSource to cancel talking
        private CancellationTokenSource chatTokenSource;

        // Actions for each status
#pragma warning disable CS1998
        public Func<Request, User, State, CancellationToken, Task> OnPromptAsync
            = async (r, u, c, t) => { Debug.LogWarning("Chatdoll.OnPromptAsync is not implemented"); };
        public Func<Request, State, CancellationToken, Task> OnNoIntentAsync
            = async (r, c, t) => { Debug.LogWarning("Chatdoll.OnNoIntentAsync is not implemented"); };
        public Func<Request, State, CancellationToken, Task> OnErrorAsync
            = async (r, c, t) => { Debug.LogWarning("Chatdoll.OnErrorAsync is not implemented"); };
#pragma warning restore CS1998

        // Awake
        public DialogController(IUserStore userStore, IStateStore stateStore, ISkillRouter skillRouter, Dictionary<RequestType, IRequestProvider> requestProviders, ModelController modelController)
        {
            UserStore = userStore;
            StateStore = stateStore;
            SkillRouter = skillRouter;
            RequestProviders = requestProviders;
            ModelController = modelController;
        }

        // Dispose
        public void Dispose()
        {
            // Stop async operations
            chatTokenSource?.Cancel();
        }

        // Start chatting loop
        public async Task StartChatAsync(string userId, bool skipPrompt = false, Request preRequest = null, Dictionary<string, object> payloads = null)
        {
            // Get cancellation token
            var token = GetChatToken();

            // Get user
            var user = await UserStore.GetUserAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.Id))
            {
                Debug.LogError($"Error occured in getting user: {userId}");
                await OnErrorAsync(null, null, token);
                return;
            }

            // Get state
            var state = await StateStore.GetStateAsync(user.Id);
            if (state == null)
            {
                Debug.LogError($"Error occured in getting state: {user.Id}");
                await OnErrorAsync(null, null, token);
                return;
            }

            // Request
            Request request = null;

            try
            {
                IsChatting = true;

                // Prompt
                if (!skipPrompt)
                {
                    await OnPromptAsync(preRequest, user, state, token);
                }

                // Chat loop. Exit when session ends, canceled or error occures
                while (true)
                {
                    if (token.IsCancellationRequested) { return; }

                    var requestProvider = preRequest == null || preRequest.Type == RequestType.None ?
                        RequestProviders[state.Topic.RequiredRequestType] :
                        RequestProviders[preRequest.Type];

                    // Get or update request (microphone / camera / QR code, etc)
                    request = await requestProvider.GetRequestAsync(user, state, token, preRequest);

                    // Clear preRequest for the next turn
                    preRequest = null;

                    if (!request.IsSet() || request.IsCanceled)
                    {
                        // Clear state when request is not set or canceled
                        state.Clear();
                        await StateStore.SaveStateAsync(state);
                        return;
                    }

                    if (token.IsCancellationRequested) { return; }

                    // Extract intent when request doesn't have intent (pre-request didn't set intent)
                    if (!request.HasIntent())
                    {
                        var intentExtractionResult = await SkillRouter.ExtractIntentAsync(request, state, token);
                        if (intentExtractionResult != null)
                        {
                            request.SetExtractionResult(intentExtractionResult);
                        }
                    }

                    if (!request.HasIntent() && string.IsNullOrEmpty(state.Topic.Name))
                    {
                        // Just exit loop without clearing state when NoIntent
                        await OnNoIntentAsync(request, state, token);
                        return;
                    }
                    else
                    {
                        Debug.Log($"Intent:{request.Intent.Name}({request.Intent.Priority.ToString()})");
                        if (request.Entities.Count > 0)
                        {
                            var entitiesString = "Entities:";
                            foreach (var kv in request.Entities)
                            {
                                var v = kv.Value != null ? kv.Value.ToString() : "null";
                                entitiesString += $"\n - {kv.Key}: {v}";
                            }
                            Debug.Log(entitiesString);
                        }
                    }
                    if (token.IsCancellationRequested) { return; }

                    // Get skill to process intent / topic
                    var skill = SkillRouter.Route(request, state, token);
                    if (token.IsCancellationRequested) { return; }

                    // PreProcess
                    var preProcessResponse = await skill.PreProcessAsync(request, state, token);

                    // Start showing waiting animation
                    var waitingAnimationTask = skill.ShowWaitingAnimationAsync(preProcessResponse, request, state, token);

                    // Process skill
                    var skillResponse = await skill.ProcessAsync(request, state, token);
                    if (token.IsCancellationRequested) { return; }

                    // Wait for waiting animation before show response of skill
                    // TODO: Enable to cancel waitingAnimation instead of await when ProcessAsync ends.
                    await waitingAnimationTask;
                    if (token.IsCancellationRequested) { return; }

                    // Show response from skill
                    await skill.ShowResponseAsync(skillResponse, request, state, token);
                    if (token.IsCancellationRequested) { return; }

                    // Save user
                    await UserStore.SaveUserAsync(user);

                    // Sava state
                    if (state.Topic.IsFinished)
                    {
                        // Clear state before save when topic doesn't continue
                        state.Clear();
                        await StateStore.SaveStateAsync(state);
                        break;
                    }
                    else
                    {
                        // Save state
                        await StateStore.SaveStateAsync(state);
                        // Update properties for the next turn
                        state.IsNew = false;
                        state.Topic.IsFirstTurn = false;
                        state.Topic.IsFinished = true;
                    }
                }
            }
            catch (Exception ex)
            {
                await StateStore.DeleteStateAsync(user.Id);
                if (!token.IsCancellationRequested)
                {
                    IsError = true;
                    Debug.LogError($"Error occured in processing chat: {ex.Message}\n{ex.StackTrace}");
                    // Stop running animation and voice then get new token to say error
                    token = GetChatToken();
                    await OnErrorAsync(request, state, token);
                }
            }
            finally
            {
                IsError = false;
                IsChatting = false;

                if (!token.IsCancellationRequested)
                {
                    // NOTE: Cancel is triggered not only when just canceled but when invoked another chat session
                    // Restart idling animation and reset face expression
                    _ = ModelController?.StartIdlingAsync();
                    _ = ModelController?.SetDefaultFace();
                }
                else
                {
                    // Clear state when canceled
                    state.Clear();
                    await StateStore.SaveStateAsync(state);
                }
            }
        }

        // Stop chat
        public void StopChat(bool waitVoice = false, bool startIdling = true)
        {
            // Cancel the tasks and dispose the token source
            if (chatTokenSource != null)
            {
                chatTokenSource.Cancel();
                chatTokenSource.Dispose();
                chatTokenSource = null;
            }

            // Stop speaking immediately if not wait
            if (!waitVoice)
            {
                ModelController?.StopSpeech();
            }

            if (startIdling)
            {
                // Start idling, default face and blink. `startIdling` is true when no successive animated voice
                _ = ModelController?.StartIdlingAsync();
                _ = ModelController?.SetDefaultFace();
                _ = ModelController?.StartBlinkAsync();
            }
        }

        // Get cancellation token for tasks invoked in chat
        private CancellationToken GetChatToken()
        {
            // Stop current chat after the phrase now speaking without starting idling animation
            StopChat(true, false);

            // Create new TokenSource and return its token
            chatTokenSource = new CancellationTokenSource();
            return chatTokenSource.Token;
        }
    }
}
