using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.Model;
using ChatdollKit.Dialog.Processor;

namespace ChatdollKit.Dialog
{
    public class DialogController
    {
        public IRequestProcessor RequestProcessor { get; private set; }
        public bool IsChatting { get; private set; }
        public bool IsError { get; private set; }
        private Dictionary<RequestType, IRequestProvider> requestProviders { get; set; }
        private ModelController modelController { get; set; }
        private CancellationTokenSource dialogTokenSource { get; set; }

        // Actions for each status
#pragma warning disable CS1998
        protected Func<DialogRequest, CancellationToken, UniTask> OnPromptAsync
            = async (r, t) => { Debug.LogWarning("DialogController.OnPromptAsync is not implemented"); };
        protected Func<Request, CancellationToken, UniTask> OnErrorAsync
            = async (r, t) => { Debug.LogWarning("DialogController.OnErrorAsync is not implemented"); };
#pragma warning restore CS1998

        public DialogController(ModelController modelController, Dictionary<RequestType, IRequestProvider> requestProviders, IRequestProcessor requestProcessor, Func<DialogRequest, CancellationToken, UniTask> onPromptAsync, Func<Request, CancellationToken, UniTask> onErrorAsync)
        {
            this.modelController = modelController;
            this.requestProviders = requestProviders;
            RequestProcessor = requestProcessor;
            OnPromptAsync = onPromptAsync;
            OnErrorAsync = onErrorAsync;
        }

        // Dispose
        public void Dispose()
        {
            // Stop async operations
            dialogTokenSource?.Cancel();
        }

        // Start chatting loop
        public async UniTask StartDialogAsync(DialogRequest dialogRequest)
        {
            // Get cancellation token
            StopDialog(true, false);
            var token = GetDialogToken();

            // Request
            Request request = null;

            try
            {
                IsChatting = true;

                // Prompt
                if (!dialogRequest.SkipPrompt)
                {
                    await OnPromptAsync(dialogRequest, token);
                }

                // Set RequestType for the first turn
                var requestType = RequestType.Voice;
                if (dialogRequest.WakeWord != null)
                {
                    requestType = dialogRequest.WakeWord.RequestType;
                }

                // Chat loop. Exit when session ends, canceled or error occures
                while (true)
                {
                    if (token.IsCancellationRequested) { return; }

                    request = dialogRequest.ToRequest();
                    if (request == null)
                    {
                        // Get request (microphone / camera / QR code, etc)
                        var requestProvider = requestProviders[requestType];
                        request = await requestProvider.GetRequestAsync(token);
                        request.ClientId = dialogRequest.ClientId;
                        request.Tokens = dialogRequest.Tokens;
                    }

                    // Process request
                    var skillResponse = await RequestProcessor.ProcessRequestAsync(request, token);

                    // Controll conversation loop
                    if (skillResponse == null || skillResponse.EndConversation)
                    {
                        break;
                    }
                    else
                    {
                        requestType = skillResponse.NextTurnRequestType;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    IsError = true;
                    Debug.LogError($"Error occured in processing chat: {ex.Message}\n{ex.StackTrace}");
                    // Stop running animation and voice then get new token to say error
                    StopDialog(true, false);
                    token = GetDialogToken();
                    await OnErrorAsync(request, token);
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
                    _ = modelController?.StartIdlingAsync();
                    _ = modelController?.SetDefaultFace();
                }
            }
        }

        // Stop chat
        public void StopDialog(bool waitVoice = false, bool startIdling = true)
        {
            // Cancel the tasks and dispose the token source
            if (dialogTokenSource != null)
            {
                dialogTokenSource.Cancel();
                dialogTokenSource.Dispose();
                dialogTokenSource = null;
            }

            // Stop speaking immediately if not wait
            if (!waitVoice)
            {
                modelController?.StopSpeech();
            }

            if (startIdling)
            {
                // Start idling, default face and blink. `startIdling` is true when no successive animated voice
                _ = modelController?.StartIdlingAsync();
                _ = modelController?.SetDefaultFace();
                _ = modelController?.StartBlinkAsync();
            }
        }

        // Get cancellation token for tasks invoked in chat
        private CancellationToken GetDialogToken()
        {
            // Create new TokenSource and return its token
            dialogTokenSource = new CancellationTokenSource();
            return dialogTokenSource.Token;
        }
    }
}
