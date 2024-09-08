using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Dialog
{
    public class DialogProcessor : MonoBehaviour
    {
        // Dialog Status
        public enum DialogStatus
        {
            Idling,
            Initializing,
            Routing,
            Processing,
            Responding,
            Finalizing,
            Error
        }
        public DialogStatus Status { get; private set; }
        private ISkillRouter skillRouter { get; set; }
        private IStateStore stateStore { get; set; }
        private CancellationTokenSource dialogTokenSource { get; set; }

        // Actions for each status
        public Func<string, Dictionary<string, object>, CancellationToken, UniTask> OnStartAsync { get; set; }
        public Func<string, Dictionary<string, object>, CancellationToken, UniTask> OnRequestRecievedAsync { get; set; }
        public Func<Response, CancellationToken, UniTask> OnResponseShownAsync { get; set; }
        public Func<bool, CancellationToken, UniTask> OnEndAsync { get; set; }
        public Func<bool, UniTask> OnStopAsync { get; set; }
        public Func<string, Dictionary<string, object>, Exception, CancellationToken, UniTask> OnErrorAsync { get; set; }

        private void Awake()
        {
            // Get components
            stateStore = gameObject.GetComponent<IStateStore>() ?? new MemoryStateStore();
            skillRouter = gameObject.GetComponent<ISkillRouter>();
            skillRouter.RegisterSkills();

            Status = DialogStatus.Idling;
        }

        // OnDestroy
        private void OnDestroy()
        {
            dialogTokenSource?.Cancel();
            dialogTokenSource?.Dispose();
            dialogTokenSource = null;
        }

        // Start dialog
        public async UniTask StartDialogAsync(string text, Dictionary<string, object> payloads = null)
        {
            if (string.IsNullOrEmpty(text) && (payloads == null || payloads.Count == 0))
            {
                return;
            }

            Status = DialogStatus.Initializing;

            // Stop running dialog and get cancellation token
            StopDialog(true);
            var token = GetDialogToken();
            var endConversation = false;

            try
            {
                if (token.IsCancellationRequested) { return; }

                UniTask OnRequestRecievedTask;
                if (OnRequestRecievedAsync != null)
                {
                    OnRequestRecievedTask = OnRequestRecievedAsync(text, payloads, token);
                }
                else
                {
                    OnRequestRecievedTask = UniTask.Delay(1);
                }

                var state = await stateStore.GetStateAsync("_");
                var request = new Request(RequestType.Voice, text);
                request.Payloads = payloads ?? new Dictionary<string, object>();

                Status = DialogStatus.Routing;

                // Extract intent for routing
                var intentExtractionResult = await skillRouter.ExtractIntentAsync(request, state, token);
                if (intentExtractionResult != null)
                {
                    request.Intent = intentExtractionResult.Intent;
                    request.Entities = intentExtractionResult.Entities;
                }
                if (token.IsCancellationRequested) { return; }

                // Get skill to process intent / topic
                var skill = skillRouter.Route(request, state, token);
                if (token.IsCancellationRequested) { return; }

                // Process skill
                Status = DialogStatus.Processing;
                var skillResponse = await skill.ProcessAsync(request, state, null, token);
                if (token.IsCancellationRequested) { return; }

                // Await before showing response
                await OnRequestRecievedTask;

                // Show response
                Status = DialogStatus.Responding;
                await skill.ShowResponseAsync(skillResponse, request, state, token);
                if (token.IsCancellationRequested) { return; }

                if (OnResponseShownAsync != null)
                {
                    await OnResponseShownAsync(skillResponse, token);
                }

                await stateStore.SaveStateAsync(state);

                endConversation = skillResponse.EndConversation;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Status = DialogStatus.Error;

                    Debug.LogError($"Error at StartDialogAsync: {ex.Message}\n{ex.StackTrace}");
                    // Stop running animation and voice then get new token to say error
                    StopDialog(true);
                    token = GetDialogToken();
                    if (OnErrorAsync != null)
                    {
                        await OnErrorAsync(text, payloads, ex, token);
                    }
                }
            }
            finally
            {
                Status = DialogStatus.Finalizing;

                if (OnEndAsync != null)
                {
                    try
                    {
                        await OnEndAsync(endConversation, token);
                    }
                    catch (Exception fex)
                    {
                        Debug.LogError($"Error in finally at StartDialogAsync: {fex.Message}\n{fex.StackTrace}");
                    }
                }

                Status = DialogStatus.Idling;
            }
        }

        // Stop chat
        public async void StopDialog(bool forSuccessiveDialog = false)
        {
            // Cancel the tasks and dispose the token source
            if (dialogTokenSource != null)
            {
                dialogTokenSource.Cancel();
                dialogTokenSource.Dispose();
                dialogTokenSource = null;
            }

            if (OnStopAsync != null)
            {
                await OnStopAsync(forSuccessiveDialog);
            }
        }

        // Get cancellation token for tasks invoked in chat
        public CancellationToken GetDialogToken()
        {
            // Create new TokenSource and return its token
            dialogTokenSource = new CancellationTokenSource();
            return dialogTokenSource.Token;
        }

        public async UniTask ClearStateAsync(string userId = null)
        {
            await stateStore.DeleteStateAsync("_"); // "_" is the default user id of legacy ChatdollKit
        }
    }
}
