using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace ChatdollKit.Dialog.Processor
{
    public class LocalRequestProcessor : IRequestProcessor
    {
        protected IUserStore UserStore { get; set; }
        protected IStateStore StateStore { get; set; }
        protected ISkillRouter SkillRouter { get; set; }
        public Func<Request, CancellationToken, UniTask> OnStartShowingWaitingAnimationAsync { get; set; }
        public Func<Response, CancellationToken, UniTask> OnStartShowingResponseAsync { get; set; }

        public LocalRequestProcessor(IUserStore userStore, IStateStore stateStore, ISkillRouter skillRouter)
        {
            UserStore = userStore ?? new MemoryUserStore();
            StateStore = stateStore ?? new MemoryStateStore();
            SkillRouter = skillRouter ?? new StaticSkillRouter();
            SkillRouter.RegisterSkills();
        }

        public virtual async UniTask<Response> ProcessRequestAsync(Request request, CancellationToken token)
        {
            string stateId = null;
            try
            {
                // Get user
                var user = await UserStore.GetUserAsync(request.ClientId);
                if (user == null)
                {
                    throw new Exception($"Error occured in getting user: {request.ClientId}");
                }
                stateId = user.Id;

                // Get state
                var state = await StateStore.GetStateAsync(stateId);
                if (state == null)
                {
                    throw new Exception($"Error occured in getting state: {stateId}");
                }

                if (!request.IsSet() || request.IsCanceled)
                {
                    // Clear state when request is not set or canceled
                    state.Clear();
                    await StateStore.SaveStateAsync(state);
                    return null;
                }

                if (token.IsCancellationRequested) { return null; }

                // Extract intent when request doesn't have intent (pre-request didn't set intent)
                if (!request.HasIntent())
                {
                    var intentExtractionResult = await SkillRouter.ExtractIntentAsync(request, state, token);
                    if (intentExtractionResult != null)
                    {
                        request.Intent = intentExtractionResult.Intent;
                        request.Entities = intentExtractionResult.Entities;
                    }
                }

                if (!request.HasIntent() && string.IsNullOrEmpty(state.Topic.Name))
                {
                    // End conversation without clearing state when intent and topic is not set
                    return null;
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
                if (token.IsCancellationRequested) { return null; }

                // Get skill to process intent / topic
                var skill = SkillRouter.Route(request, state, token);
                if (token.IsCancellationRequested) { return null; }

                // PreProcess
                var preProcessResponse = await skill.PreProcessAsync(request, state, token);

                // Start showing waiting animation
                if (OnStartShowingWaitingAnimationAsync != null)
                {
                    await OnStartShowingWaitingAnimationAsync(request, token);
                }
                var waitingAnimationTask = skill.ShowWaitingAnimationAsync(preProcessResponse, request, state, token);

                // Process skill
                var skillResponse = await skill.ProcessAsync(request, state, user, token);
                if (token.IsCancellationRequested) { return null; }

                // Wait for waiting animation before show response of skill
                // TODO: Enable to cancel waitingAnimation instead of await when ProcessAsync ends.
                await waitingAnimationTask;
                if (token.IsCancellationRequested) { return null; }

                // Show response from skill
                if (OnStartShowingResponseAsync != null)
                {
                    await OnStartShowingResponseAsync(skillResponse, token);
                }

                await skill.ShowResponseAsync(skillResponse, request, state, token);
                if (token.IsCancellationRequested) { return null; }

                // Save user
                await UserStore.SaveUserAsync(user);

                // Save state
                if (skillResponse.EndTopic || skillResponse.EndConversation)
                {
                    // Clear state before save when topic doesn't continue
                    state.Clear();
                }
                else
                {
                    // Update properties for the next turn
                    state.Topic.IsFirstTurn = false;
                }
                await StateStore.SaveStateAsync(state);

                // Continue the previous topic when request is adhoc
                if (request.Intent.IsAdhoc && !string.IsNullOrEmpty(state.Topic.Name))
                {
                    skillResponse.EndTopic = false;
                }

                return skillResponse;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in processing request: {ex.Message}\n{ex.StackTrace}");

                if (stateId != null)
                {
                    await StateStore.DeleteStateAsync(stateId);
                }

                throw ex;
            }
        }
    }
}
