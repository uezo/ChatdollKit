using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ChatdollKit.LLM;

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
        private string processingId { get; set; }
        private CancellationTokenSource dialogTokenSource { get; set; }

        // Actions for each status
        public Func<string, Dictionary<string, object>, CancellationToken, UniTask> OnStartAsync { get; set; }
        public Func<string, Dictionary<string, object>, CancellationToken, UniTask> OnRequestRecievedAsync { get; set; }
        public Func<ILLMSession, CancellationToken, UniTask> OnBeforeProcessContentStreamAsync { get; set; }
        public Func<string, Dictionary<string, object>, ILLMSession, CancellationToken, UniTask> OnResponseShownAsync { get; set; }
        public Func<bool, CancellationToken, UniTask> OnEndAsync { get; set; }
        public Func<bool, UniTask> OnStopAsync { get; set; }
        public Func<string, Dictionary<string, object>, Exception, CancellationToken, UniTask> OnErrorAsync { get; set; }

        // LLM
        private ILLMService llmService { get; set; }
        private LLMContentProcessor llmContentProcessor { get; set; }
        private Dictionary<string, ITool> toolResolver { get; set; } = new Dictionary<string, ITool>();
        private List<ILLMTool> toolSpecs { get; set; } = new List<ILLMTool>();
        public LLMServiceExtensions LLMServiceExtensions { get; } = new LLMServiceExtensions();
        public ILLMService LLMService { get { return llmService; }}

        // Merge consecutive requests
        [SerializeField]
        private float mergeRequestThreshold = 0.0f;
        [SerializeField]
        private string mergeRequestPrefix = "Previous user's request and your response have been canceled. Please respond again to the following request:";
        private string previousRequestText;
        private DateTime previousRequestAt = DateTime.MinValue;

        private void Awake()
        {
            // Select enabled LLMService
            SelectLLMService();
            Debug.Log($"LLMService: {llmService}");

            llmContentProcessor = GetComponent<LLMContentProcessor>();

            // Register tool to toolResolver and its spec to toolSpecs
            LoadLLMTools();

            Status = DialogStatus.Idling;
        }

        // OnDestroy
        private void OnDestroy()
        {
            dialogTokenSource?.Cancel();
            dialogTokenSource?.Dispose();
            dialogTokenSource = null;
        }

        public void SelectLLMService(ILLMService llmService = null)
        {
            var llmServices = gameObject.GetComponents<ILLMService>();

            if (llmService != null)
            {
                this.llmService = llmService;
                foreach (var llms in llmServices)
                {
                    llms.IsEnabled = llms == llmService;
                }
                return;
            }

            if (llmServices.Length == 0)
            {
                Debug.LogError($"No LLMServices found");
                return;
            }

            foreach (var llms in llmServices)
            {
                if (llms.IsEnabled)
                {
                    this.llmService = llms;
                    return;
                }
            }

            Debug.LogWarning($"No enabled LLMServices found. Enable {llmServices[0]} to use.");
            llmServices[0].IsEnabled = true;
            this.llmService = llmServices[0];
        }

        public void LoadLLMTools()
        {
            toolResolver.Clear();
            toolSpecs.Clear();
            foreach (var tool in gameObject.GetComponents<ITool>())
            {
                var toolSpec = tool.GetToolSpec();
                toolResolver.Add(toolSpec.name, tool);
                toolSpecs.Add(toolSpec);
            }            
        }

        // Start dialog
        public async UniTask StartDialogAsync(string text, Dictionary<string, object> payloads = null, bool overwrite = true, bool endConversation = false)
        {
            if (string.IsNullOrEmpty(text) && (payloads == null || payloads.Count == 0))
            {
                return;
            }

            Status = DialogStatus.Initializing;
            processingId = Guid.NewGuid().ToString();
            var currentProcessingId = processingId;

            if (overwrite)
            {
                // Stop running dialog and get cancellation token
                await StopDialog(true);
            }

            var token = GetDialogToken();

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

                // A little complex to keep compatibility with v0.7.x
                var llmPayloads = new Dictionary<string, object>()
                {
                    {"RequestPayloads", payloads ?? new Dictionary<string, object>()}
                };

                // Configure LLMService
                llmService.Tools = toolSpecs;
                LLMServiceExtensions.SetExtentions(llmService);

                // Merge consecutive requests
                if (mergeRequestThreshold > 0)
                {
                    var now = DateTime.UtcNow;
                    var requestInterval = (now - previousRequestAt).TotalSeconds;
                    if (mergeRequestThreshold > requestInterval)
                    {
                        Debug.Log($"Merge consecutive requests: Interval {requestInterval} < Threshold {mergeRequestThreshold}");
                        text = previousRequestText + "\n" + text;
                        if (!text.StartsWith(mergeRequestPrefix))
                        {
                            text = mergeRequestPrefix + "\n\n" + text;
                        }
                    }
                    previousRequestText = text;
                    previousRequestAt = now;
                }

                // Call LLM
                Status = DialogStatus.Processing;
                var messages = await llmService.MakePromptAsync("_", text, llmPayloads, token);
                var llmSession = await llmService.GenerateContentAsync(messages, llmPayloads, token: token);

                if (OnBeforeProcessContentStreamAsync != null)
                {
                    await OnBeforeProcessContentStreamAsync(llmSession, token);
                }

                // Start parsing voices, faces and animations
                var processContentStreamTask = llmContentProcessor.ProcessContentStreamAsync(llmSession, token);

                // Await thinking performance before showing response
                await OnRequestRecievedTask;

                // Show response
                Status = DialogStatus.Responding;
                var showContentTask = llmContentProcessor.ShowContentAsync(llmSession, token);

                // Wait for API stream ends
                await llmSession.StreamingTask;
                if (llmService.OnStreamingEnd != null)
                {
                    await llmService.OnStreamingEnd(text, payloads, llmSession, token);
                }

                // Wait parsing and performance
                await processContentStreamTask;
                await showContentTask;

                if (token.IsCancellationRequested) { return; }

                if (OnResponseShownAsync != null)
                {
                    await OnResponseShownAsync(text, payloads, llmSession, token);
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Status = DialogStatus.Error;

                    Debug.LogError($"Error at StartDialogAsync: {ex.Message}\n{ex.StackTrace}");
                    // Stop running animation and voice then get new token to say error
                    await StopDialog(true);
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

                if (currentProcessingId == processingId)
                {
                    // Reset status when another dialog is not started
                    Status = DialogStatus.Idling;
                }
            }
        }

        // Stop chat
        public async UniTask StopDialog(bool forSuccessiveDialog = false, bool waitForIdling = false)
        {
            // Cancel the tasks and dispose the token source
            if (dialogTokenSource != null)
            {
                dialogTokenSource.Cancel();
                dialogTokenSource.Dispose();
                dialogTokenSource = null;
            }

            if (waitForIdling)
            {
                var startTime = Time.time;
                while (Status != DialogStatus.Idling)
                {
                    if (Time.time - startTime > 1.0f)
                    {
                        Debug.LogWarning($"Dialog status doesn't change to idling in 1 second. (Status: {Status})");
                        break;
                    }
                    await UniTask.Delay(10);
                }
            }

            if (OnStopAsync != null)
            {
                await OnStopAsync(forSuccessiveDialog);
            }
        }

        // LLM Context management
        public List<ILLMMessage> GetContext(int count)
        {
            return llmService?.GetContext(count);
        }

        public void ClearContext()
        {
            llmService?.ClearContext();
        }

        // Get cancellation token for tasks invoked in chat
        public CancellationToken GetDialogToken()
        {
            // Create new TokenSource and return its token
            dialogTokenSource = new CancellationTokenSource();
            return dialogTokenSource.Token;
        }
    }

    public class LLMServiceExtensions
    {
        public Action <Dictionary<string, string>, ILLMSession> HandleExtractedTags { get; set; }
        public Func<string, UniTask<byte[]>> CaptureImage { get; set; }
        public Func<string, Dictionary<string, object>, ILLMSession, CancellationToken, UniTask> OnStreamingEnd { get; set; }

        public void SetExtentions(ILLMService llmService)
        {
            llmService.HandleExtractedTags = HandleExtractedTags;
            llmService.CaptureImage = CaptureImage;
            llmService.OnStreamingEnd = OnStreamingEnd;
        }
    }
}
